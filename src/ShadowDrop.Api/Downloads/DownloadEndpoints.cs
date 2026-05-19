// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Downloads;

using Microsoft.Net.Http.Headers;
using ShadowDrop.Api.Configuration;
using ShadowDrop.Contracts;

public static class DownloadEndpoints
{
    private const String PlaintextEndExclusiveQueryParameterName = "plaintextEndExclusive";
    private const String PlaintextStartQueryParameterName = "plaintextStart";

    public static WebApplication MapDownloadEndpoints(this WebApplication app, ShadowDropOptions options)
    {
        if (options.ApiExposure.EnablePublicDownloads)
        {
            var downloadRoutes = app.MapGroup("/d");
            downloadRoutes.MapGet("/{token}/files/{fileId:guid}", DownloadAsync);
        }

        return app;
    }

    private static Boolean ContainsControlCharacter(String value)
    {
        foreach (var character in value)
        {
            if (Char.IsControl(character) || character is >= '\u0080' and <= '\u009F')
            {
                return true;
            }
        }

        return false;
    }

    private static async Task<IResult> DownloadAsync(String token,
                                                     Guid fileId,
                                                     HttpRequest request,
                                                     DownloadFileService downloadFileService,
                                                     ILoggerFactory loggerFactory,
                                                     CancellationToken cancellationToken)
    {
        var downloadRequest = TryCreateDownloadRequest(token, fileId, request);
        if (downloadRequest is null)
        {
            return new StatusDownloadResult(StatusCodes.Status400BadRequest, "{\"error\":\"Invalid download request.\"}");
        }

        var result = await downloadFileService.ResolveAsync(downloadRequest, cancellationToken);
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

    private static String GetResponseContentType(String? contentType) =>
        !String.IsNullOrWhiteSpace(contentType) && MediaTypeHeaderValue.TryParse(contentType, out _)
            ? contentType
            : "application/octet-stream";

    private static RequestedByteRange? ParseCliRangeHeader(String rangeHeader)
    {
        if (!RangeHeaderValue.TryParse(rangeHeader, out var parsedRange)
            || !String.Equals(parsedRange.Unit.ToString(), "bytes", StringComparison.OrdinalIgnoreCase)
            || parsedRange.Ranges.Count != 1)
        {
            return null;
        }

        var range = parsedRange.Ranges.Single();
        if (range.From is null || range.To is null || range.From < 0 || range.To < range.From)
        {
            return null;
        }

        return new(range.From.Value, range.To.Value);
    }

    private static RequestedByteRange? ParseDirectHttpRangeHeader(String rangeHeader)
    {
        if (!RangeHeaderValue.TryParse(rangeHeader, out var parsedRange)
            || !String.Equals(parsedRange.Unit.ToString(), "bytes", StringComparison.OrdinalIgnoreCase)
            || parsedRange.Ranges.Count != 1)
        {
            return null;
        }

        var range = parsedRange.Ranges.Single();
        if (range.From is null && range.To is null)
        {
            return null;
        }

        if (range.From is not null && range.From < 0)
        {
            return null;
        }

        if (range.From is not null && range.To is not null && range.To < range.From)
        {
            return null;
        }

        return new(range.From, range.To);
    }

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

    private static DownloadRequest? TryCreateDownloadRequest(String token, Guid fileId, HttpRequest request)
    {
        var authorizationBearerToken = TryGetBearerToken(request);
        var headerKeyMaterial = request.Headers[DownloadKeyConstants.HeaderName].ToString();
        var queryKeyMaterial = request.Query[DownloadKeyConstants.QueryParameterName].ToString();
        var rangeHeader = request.Headers.Range.ToString();
        var mode = request.Query[DownloadHeaderConstants.ModeQueryParameterName].ToString();
        var hasLegacyRangeQuery = request.Query.ContainsKey(PlaintextStartQueryParameterName)
                                  || request.Query.ContainsKey(PlaintextEndExclusiveQueryParameterName);
        if (hasLegacyRangeQuery)
        {
            return null;
        }

        if (request.Query.ContainsKey(DownloadHeaderConstants.ModeQueryParameterName) && String.IsNullOrWhiteSpace(mode))
        {
            return null;
        }

        if (String.IsNullOrWhiteSpace(mode))
        {
            var requestedRange = String.IsNullOrWhiteSpace(rangeHeader)
                ? null
                : ParseDirectHttpRangeHeader(rangeHeader);
            if (!String.IsNullOrWhiteSpace(rangeHeader) && requestedRange is null)
            {
                return null;
            }

            return new(DownloadRequestMode.DirectHttp,
                       token,
                       fileId,
                       authorizationBearerToken,
                       headerKeyMaterial,
                       queryKeyMaterial,
                       requestedRange);
        }

        if (!String.Equals(mode, DownloadHeaderConstants.StreamedCliMode, StringComparison.OrdinalIgnoreCase)
            || !String.IsNullOrWhiteSpace(headerKeyMaterial)
            || !String.IsNullOrWhiteSpace(queryKeyMaterial))
        {
            return null;
        }

        var cliRange = String.IsNullOrWhiteSpace(rangeHeader)
            ? null
            : ParseCliRangeHeader(rangeHeader);
        if (!String.IsNullOrWhiteSpace(rangeHeader) && cliRange is null)
        {
            return null;
        }

        return new(DownloadRequestMode.Cli,
                   token,
                   fileId,
                   authorizationBearerToken,
                   headerKeyMaterial,
                   queryKeyMaterial,
                   cliRange);
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
            var sanitizedFileName = SanitizeFileName(resolution.FileName);
            httpContext.Response.StatusCode = resolution.Mode == DownloadMode.DirectHttp && resolution.RequestedRange is not null
                ? StatusCodes.Status206PartialContent
                : StatusCodes.Status200OK;
            httpContext.Response.ContentType = GetResponseContentType(resolution.ResponseContentType);
            httpContext.Response.ContentLength = resolution.ResponseContentLength;
            httpContext.Response.Headers[DownloadHeaderConstants.FileNameHeaderName] = sanitizedFileName;
            httpContext.Response.Headers[DownloadHeaderConstants.FileContentTypeHeaderName] = SanitizeHeaderValue(resolution.FileContentType);
            httpContext.Response.Headers[DownloadHeaderConstants.ModeHeaderName] = resolution.Mode == DownloadMode.DirectHttp
                ? "direct-http"
                : DownloadHeaderConstants.StreamedCliMode;

            if (resolution.Mode == DownloadMode.DirectHttp)
            {
                httpContext.Response.Headers.AcceptRanges = "bytes";
                if (resolution.RequestedRange is not null)
                {
                    httpContext.Response.Headers.ContentRange = new ContentRangeHeaderValue(resolution.RequestedRange.Start,
                                                                                            resolution.RequestedRange.End - 1,
                                                                                            resolution.TotalPlaintextLength).ToString();
                }

                httpContext.Response.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment")
                {
                    FileNameStar = sanitizedFileName
                }.ToString();
            }
            else if (resolution.CliMetadata is not null)
            {
                httpContext.Response.Headers[DownloadHeaderConstants.FirstChunkIndexHeaderName] = resolution.CliMetadata.FirstChunkIndex.ToString();
                httpContext.Response.Headers[DownloadHeaderConstants.LastChunkIndexHeaderName] = resolution.CliMetadata.LastChunkIndex.ToString();
                httpContext.Response.Headers[DownloadHeaderConstants.PlaintextRangeStartHeaderName] = resolution.CliMetadata.RequestedRange.Start.ToString();
                httpContext.Response.Headers[DownloadHeaderConstants.PlaintextRangeEndHeaderName] = resolution.CliMetadata.RequestedRange.End.ToString();
                httpContext.Response.Headers[DownloadHeaderConstants.TotalPlaintextSizeHeaderName] = resolution.CliMetadata.TotalPlaintextSize.ToString();
                httpContext.Response.Headers[DownloadHeaderConstants.ChunkSizeHeaderName] = resolution.CliMetadata.ChunkSize.ToString();
                httpContext.Response.Headers[DownloadHeaderConstants.FinalChunkPlaintextLengthHeaderName] =
                    resolution.CliMetadata.FinalChunkPlaintextLength.ToString();
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
            if (statusCode != StatusCodes.Status416RangeNotSatisfiable)
            {
                httpContext.Response.Headers.AcceptRanges = "bytes";
            }

            if (body is null)
            {
                return;
            }

            httpContext.Response.ContentType = "application/json";
            await httpContext.Response.WriteAsync(body, httpContext.RequestAborted);
        }
    }
}
