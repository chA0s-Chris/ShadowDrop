// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Downloads;

using Microsoft.Net.Http.Headers;
using ShadowDrop.Api.Configuration;
using ShadowDrop.Contracts;

public static class DownloadEndpoints
{
    public static WebApplication MapDownloadEndpoints(this WebApplication app, ShadowDropOptions options)
    {
        if (options.ApiExposure.EnablePublicDownloads)
        {
            var downloadRoutes = app.MapGroup("/d");
            downloadRoutes.MapGet("/{token}/files/{fileId:guid}", DownloadAsync);
        }

        return app;
    }

    private static async Task<IResult> DownloadAsync(String token,
                                                     Guid fileId,
                                                     HttpRequest request,
                                                     DownloadFileService downloadFileService,
                                                     ILoggerFactory loggerFactory,
                                                     CancellationToken cancellationToken)
    {
        var authorizationBearerToken = TryGetBearerToken(request);
        var headerKeyMaterial = request.Headers[DownloadKeyConstants.HeaderName].ToString();
        var queryKeyMaterial = request.Query[DownloadKeyConstants.QueryParameterName].ToString();
        var result = await downloadFileService.ResolveAsync(token,
                                                            fileId,
                                                            authorizationBearerToken,
                                                            headerKeyMaterial,
                                                            queryKeyMaterial,
                                                            cancellationToken);

        if (result.Status != DownloadLookupStatus.Success)
        {
            loggerFactory.CreateLogger(typeof(DownloadEndpoints))
                         .LogInformation("Public download request failed with status {Status} for file {FileId}.",
                                         result.Status,
                                         fileId);
        }

        return result.Status switch
        {
            DownloadLookupStatus.Success => new DownloadStreamResult(result.Resolution!),
            DownloadLookupStatus.InvalidShare or DownloadLookupStatus.ExpiredShare => Results.Unauthorized(),
            DownloadLookupStatus.Forbidden => Results.StatusCode(StatusCodes.Status403Forbidden),
            DownloadLookupStatus.NotFound => Results.NotFound(),
            DownloadLookupStatus.InvalidRequest => Results.BadRequest(new
            {
                Error = "Invalid download request."
            }),
            _ => Results.StatusCode(StatusCodes.Status500InternalServerError)
        };
    }

    private static String? TryGetBearerToken(HttpRequest request)
    {
        var authorizationValue = request.Headers.Authorization.ToString();
        const String prefix = "Bearer ";
        return authorizationValue.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? authorizationValue[prefix.Length..].Trim()
            : null;
    }

    private sealed class DownloadStreamResult(DownloadFileResolution resolution) : IResult
    {
        public async Task ExecuteAsync(HttpContext httpContext)
        {
            httpContext.Response.ContentType = resolution.ContentType;
            httpContext.Response.ContentLength = resolution.ContentLength;
            httpContext.Response.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment")
            {
                FileNameStar = resolution.FileName
            }.ToString();
            httpContext.Response.Headers["X-ShadowDrop-Download-Mode"] = resolution.Mode == DownloadMode.DirectHttp
                ? "direct-http"
                : "cli-decrypt";

            await using var contentStream = resolution.ContentStream;
            await contentStream.CopyToAsync(httpContext.Response.Body, httpContext.RequestAborted);
        }
    }
}
