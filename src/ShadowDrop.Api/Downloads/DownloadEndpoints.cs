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
        var rangeHeader = request.Headers.Range.ToString();
        var plaintextStart = TryGetInt64QueryValue(request.Query, "plaintextStart");
        var plaintextEndExclusive = TryGetInt64QueryValue(request.Query, "plaintextEndExclusive");
        var result = await downloadFileService.ResolveAsync(token,
                                                            fileId,
                                                            authorizationBearerToken,
                                                            headerKeyMaterial,
                                                            queryKeyMaterial,
                                                            rangeHeader,
                                                            plaintextStart,
                                                            plaintextEndExclusive,
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
            DownloadLookupStatus.InvalidShare or DownloadLookupStatus.ExpiredShare => new StatusDownloadResult(StatusCodes.Status401Unauthorized),
            DownloadLookupStatus.Forbidden => new StatusDownloadResult(StatusCodes.Status403Forbidden),
            DownloadLookupStatus.NotFound => new StatusDownloadResult(StatusCodes.Status404NotFound),
            DownloadLookupStatus.InvalidRequest or DownloadLookupStatus.InvalidRange => new StatusDownloadResult(StatusCodes.Status400BadRequest,
                "{\"error\":\"Invalid download request.\"}"),
            DownloadLookupStatus.RangeNotSatisfiable => new StatusDownloadResult(StatusCodes.Status416RangeNotSatisfiable),
            _ => new StatusDownloadResult(StatusCodes.Status500InternalServerError)
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

    private static Int64? TryGetInt64QueryValue(IQueryCollection query, String key)
    {
        var rawValue = query[key].ToString();
        return String.IsNullOrWhiteSpace(rawValue)
            ? null
            : Int64.TryParse(rawValue, out var parsedValue)
                ? parsedValue
                : Int64.MinValue;
    }

    private sealed class DownloadStreamResult(DownloadFileResolution resolution) : IResult
    {
        private static Boolean ContainsControlCharacter(String value)
        {
            foreach (var character in value)
            {
                if (Char.IsControl(character) || (character >= '\u0080' && character <= '\u009F'))
                {
                    return true;
                }
            }

            return false;
        }

        private static String GetResponseContentType(String? contentType) =>
            !String.IsNullOrWhiteSpace(contentType) && MediaTypeHeaderValue.TryParse(contentType, out _)
                ? contentType
                : "application/octet-stream";

        private static String SanitizeFileName(String? value)
        {
            var sanitized = SanitizeHeaderValue(value);
            return sanitized.Length == 0 ? "download" : sanitized;
        }

        private static String SanitizeHeaderValue(String? value)
        {
            if (String.IsNullOrWhiteSpace(value))
            {
                return String.Empty;
            }

            var sanitized = ContainsControlCharacter(value)
                ? String.Create(value.Length, value, static (destination, source) =>
                {
                    var index = 0;
                    foreach (var character in source)
                    {
                        if (!Char.IsControl(character))
                        {
                            destination[index++] = character;
                        }
                    }

                    destination[index..].Clear();
                }).TrimEnd('\0')
                : value;
            return sanitized.Length > 500 ? sanitized[..500] : sanitized;
        }

        public async Task ExecuteAsync(HttpContext httpContext)
        {
            var sanitizedFileName = SanitizeFileName(resolution.FileName);
            httpContext.Response.StatusCode = resolution.RequestedRange is null
                ? StatusCodes.Status200OK
                : StatusCodes.Status206PartialContent;
            httpContext.Response.ContentType = GetResponseContentType(resolution.ResponseContentType);
            httpContext.Response.ContentLength = resolution.ResponseContentLength;
            httpContext.Response.Headers.AcceptRanges = "bytes";
            httpContext.Response.Headers[DownloadHeaderConstants.FileNameHeaderName] = sanitizedFileName;
            httpContext.Response.Headers[DownloadHeaderConstants.FileContentTypeHeaderName] = SanitizeHeaderValue(resolution.FileContentType);
            httpContext.Response.Headers[DownloadHeaderConstants.ModeHeaderName] = resolution.Mode == DownloadMode.DirectHttp
                ? "direct-http"
                : "cli-decrypt";

            if (resolution.RequestedRange is not null)
            {
                httpContext.Response.Headers.ContentRange = new ContentRangeHeaderValue(resolution.RequestedRange.Start,
                                                                                        resolution.RequestedRange.End - 1,
                                                                                        resolution.TotalPlaintextLength).ToString();
            }

            if (resolution.Mode == DownloadMode.DirectHttp)
            {
                httpContext.Response.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment")
                {
                    FileNameStar = sanitizedFileName
                }.ToString();
            }

            await using var contentStream = resolution.ContentStream;
            await contentStream.CopyToAsync(httpContext.Response.Body, httpContext.RequestAborted);
        }
    }

    private sealed class StatusDownloadResult(Int32 statusCode, String? body = null) : IResult
    {
        public async Task ExecuteAsync(HttpContext httpContext)
        {
            httpContext.Response.StatusCode = statusCode;
            httpContext.Response.Headers.AcceptRanges = "bytes";

            if (body is null)
            {
                return;
            }

            httpContext.Response.ContentType = "application/json";
            await httpContext.Response.WriteAsync(body, httpContext.RequestAborted);
        }
    }
}
