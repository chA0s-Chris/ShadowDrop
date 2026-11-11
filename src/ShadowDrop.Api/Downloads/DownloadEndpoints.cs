// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Downloads;

using Microsoft.Net.Http.Headers;
using ShadowDrop.Api.Configuration;
using ShadowDrop.Contracts;
using System.Globalization;
using System.Text;

public static class DownloadEndpoints
{
    // Matches the common file system limit for a single file name and keeps every encoded header well below reverse-proxy
    // header buffers such as nginx's default proxy_buffer_size.
    private const Int32 MaxHeaderValueUtf8ByteCount = 255;
    private const String PlaintextEndExclusiveQueryParameterName = "plaintextEndExclusive";
    private const String PlaintextStartQueryParameterName = "plaintextStart";

    public static WebApplication MapDownloadEndpoints(this WebApplication app, ShadowDropOptions options)
    {
        if (options.ApiExposure.EnablePublicDownloads)
        {
            var downloadRoutes = app.MapGroup("/d");
            downloadRoutes.MapGet("/{token}", GetShareManifestAsync);
            downloadRoutes.MapGet("/{token}/files/{fileId:guid}", DownloadAsync);
        }

        return app;
    }

    private static async Task<IResult> DownloadAsync(String token,
                                                     Guid fileId,
                                                     HttpRequest request,
                                                     DownloadFileService downloadFileService,
                                                     CancellationToken cancellationToken)
    {
        var downloadRequest = TryCreateDownloadRequest(token, fileId, request);
        if (downloadRequest is null)
        {
            return new StatusDownloadResult(StatusCodes.Status400BadRequest, "{\"error\":\"Invalid download request.\"}");
        }

        // DownloadFileService logs every non-success resolution with the failure reason and safe share/file context.
        var result = await downloadFileService.ResolveAsync(downloadRequest, cancellationToken);

        return result.Status switch
        {
            DownloadLookupStatus.Success when result.Resolution is { } resolution => new DownloadStreamResult(resolution),
            DownloadLookupStatus.InvalidShare => new StatusDownloadResult(StatusCodes.Status401Unauthorized),
            DownloadLookupStatus.Forbidden => new StatusDownloadResult(StatusCodes.Status403Forbidden),
            DownloadLookupStatus.NotFound => new StatusDownloadResult(StatusCodes.Status404NotFound),
            DownloadLookupStatus.InvalidRequest or DownloadLookupStatus.InvalidRange => new StatusDownloadResult(StatusCodes.Status400BadRequest,
                                                                                                                 "{\"error\":\"Invalid download request.\"}"),
            DownloadLookupStatus.RangeNotSatisfiable => new StatusDownloadResult(StatusCodes.Status416RangeNotSatisfiable),
            _ => new StatusDownloadResult(StatusCodes.Status500InternalServerError)
        };
    }

    // The mirrored metadata headers carry percent-encoded UTF-8 because Kestrel rejects non-ASCII response header characters.
    private static String EncodeMirroredHeaderValue(String sanitizedValue) => Uri.EscapeDataString(sanitizedValue);

    private static String FormatInvariantHeaderValue(Int64 value) => value.ToString(CultureInfo.InvariantCulture);

    private static String FormatInvariantHeaderValue(Int32 value) => value.ToString(CultureInfo.InvariantCulture);

    private static String GetResponseContentType(String? contentType) =>
        !String.IsNullOrWhiteSpace(contentType) && IsKestrelSafeHeaderValue(contentType) && MediaTypeHeaderValue.TryParse(contentType, out _)
            ? contentType
            : "application/octet-stream";

    private static async Task<IResult> GetShareManifestAsync(String token,
                                                             HttpRequest request,
                                                             DownloadFileService downloadFileService,
                                                             CancellationToken cancellationToken)
    {
        // DownloadFileService logs every non-success resolution with the failure reason and safe share/file context.
        var result = await downloadFileService.ResolveManifestAsync(token, TryGetBearerToken(request), cancellationToken);

        return result.Status switch
        {
            DownloadLookupStatus.Success => new NoStoreResult(Results.Json(result.Manifest)),
            DownloadLookupStatus.InvalidShare => new StatusDownloadResult(StatusCodes.Status401Unauthorized),
            DownloadLookupStatus.Forbidden => new StatusDownloadResult(StatusCodes.Status403Forbidden),
            DownloadLookupStatus.NotFound => new StatusDownloadResult(StatusCodes.Status404NotFound),
            _ => new StatusDownloadResult(StatusCodes.Status500InternalServerError)
        };
    }

    // MediaTypeHeaderValue accepts non-ASCII and control characters inside quoted parameters, but Kestrel only accepts visible ASCII,
    // space, and horizontal tab in a response header value.
    private static Boolean IsKestrelSafeHeaderValue(String value)
    {
        foreach (var character in value)
        {
            if (character is (< ' ' and not '\t') or > '~')
            {
                return false;
            }
        }

        return true;
    }

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

        return new RequestedByteRange(range.From.Value, range.To.Value);
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

        return new RequestedByteRange(range.From, range.To);
    }

    private static String SanitizeFileName(String? value)
    {
        var sanitized = SanitizeHeaderValue(value);
        return sanitized.Length == 0 ? "download" : sanitized;
    }

    /// <summary>
    /// Removes control characters (C0, DEL, and C1) and limits the value to <see cref="MaxHeaderValueUtf8ByteCount"/> UTF-8 bytes
    /// without splitting a character. Ill-formed UTF-16 is replaced with U+FFFD so the result always round-trips through UTF-8.
    /// </summary>
    private static String SanitizeHeaderValue(String? value)
    {
        if (String.IsNullOrWhiteSpace(value))
        {
            return String.Empty;
        }

        var builder = new StringBuilder(Math.Min(value.Length, MaxHeaderValueUtf8ByteCount));
        Span<Char> utf16Buffer = stackalloc Char[2];
        var utf8ByteCount = 0;
        foreach (var rune in value.EnumerateRunes())
        {
            if (Rune.IsControl(rune))
            {
                continue;
            }

            utf8ByteCount += rune.Utf8SequenceLength;
            if (utf8ByteCount > MaxHeaderValueUtf8ByteCount)
            {
                break;
            }

            builder.Append(utf16Buffer[..rune.EncodeToUtf16(utf16Buffer)]);
        }

        return builder.ToString();
    }

    private static void SetNoStoreCacheControl(HttpContext httpContext)
    {
        httpContext.Response.GetTypedHeaders().CacheControl = new CacheControlHeaderValue
        {
            NoStore = true
        };
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

            return new DownloadRequest(DownloadRequestMode.DirectHttp,
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

        return new DownloadRequest(DownloadRequestMode.Cli,
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

    private sealed class DownloadStreamResult : IResult
    {
        private readonly DownloadFileResolution _resolution;

        public DownloadStreamResult(DownloadFileResolution resolution)
        {
            _resolution = resolution;
        }

        public async Task ExecuteAsync(HttpContext httpContext)
        {
            var sanitizedFileName = SanitizeFileName(_resolution.FileName);
            httpContext.Response.StatusCode = _resolution.Mode == DownloadMode.DirectHttp && _resolution.RequestedRange is not null
                ? StatusCodes.Status206PartialContent
                : StatusCodes.Status200OK;
            httpContext.Response.ContentType = GetResponseContentType(_resolution.ResponseContentType);
            httpContext.Response.ContentLength = _resolution.ResponseContentLength;
            httpContext.Response.Headers[DownloadHeaderConstants.FileNameHeaderName] = EncodeMirroredHeaderValue(sanitizedFileName);
            httpContext.Response.Headers[DownloadHeaderConstants.FileContentTypeHeaderName] =
                EncodeMirroredHeaderValue(SanitizeHeaderValue(_resolution.FileContentType));
            httpContext.Response.Headers[DownloadHeaderConstants.ModeHeaderName] = _resolution.Mode == DownloadMode.DirectHttp
                ? "direct-http"
                : DownloadHeaderConstants.StreamedCliMode;

            if (_resolution.Mode == DownloadMode.DirectHttp)
            {
                SetNoStoreCacheControl(httpContext);
                httpContext.Response.Headers.AcceptRanges = "bytes";
                if (_resolution.RequestedRange is not null)
                {
                    httpContext.Response.Headers.ContentRange = new ContentRangeHeaderValue(_resolution.RequestedRange.Start,
                                                                                            _resolution.RequestedRange.End - 1,
                                                                                            _resolution.TotalPlaintextLength).ToString();
                }

                httpContext.Response.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment")
                {
                    FileNameStar = sanitizedFileName
                }.ToString();
            }
            else if (_resolution.CliMetadata is not null)
            {
                httpContext.Response.Headers[DownloadHeaderConstants.FirstChunkIndexHeaderName] =
                    FormatInvariantHeaderValue(_resolution.CliMetadata.FirstChunkIndex);
                httpContext.Response.Headers[DownloadHeaderConstants.LastChunkIndexHeaderName] =
                    FormatInvariantHeaderValue(_resolution.CliMetadata.LastChunkIndex);
                httpContext.Response.Headers[DownloadHeaderConstants.PlaintextRangeStartHeaderName] =
                    FormatInvariantHeaderValue(_resolution.CliMetadata.RequestedRange.Start);
                httpContext.Response.Headers[DownloadHeaderConstants.PlaintextRangeEndHeaderName] =
                    FormatInvariantHeaderValue(_resolution.CliMetadata.RequestedRange.End);
                httpContext.Response.Headers[DownloadHeaderConstants.TotalPlaintextSizeHeaderName] =
                    FormatInvariantHeaderValue(_resolution.CliMetadata.TotalPlaintextSize);
                httpContext.Response.Headers[DownloadHeaderConstants.ChunkSizeHeaderName] = FormatInvariantHeaderValue(_resolution.CliMetadata.ChunkSize);
                httpContext.Response.Headers[DownloadHeaderConstants.FinalChunkPlaintextLengthHeaderName] =
                    FormatInvariantHeaderValue(_resolution.CliMetadata.FinalChunkPlaintextLength);
            }

            await using var contentStream = _resolution.ContentStream;
            await contentStream.CopyToAsync(httpContext.Response.Body, httpContext.RequestAborted);
        }
    }

    private sealed class NoStoreResult : IResult
    {
        private readonly IResult _innerResult;

        public NoStoreResult(IResult innerResult)
        {
            _innerResult = innerResult;
        }

        public async Task ExecuteAsync(HttpContext httpContext)
        {
            SetNoStoreCacheControl(httpContext);
            await _innerResult.ExecuteAsync(httpContext);
        }
    }

    private sealed class StatusDownloadResult : IResult
    {
        private readonly String? _body;
        private readonly Int32 _statusCode;

        public StatusDownloadResult(Int32 statusCode, String? body = null)
        {
            _statusCode = statusCode;
            _body = body;
        }

        public async Task ExecuteAsync(HttpContext httpContext)
        {
            httpContext.Response.StatusCode = _statusCode;
            if (_statusCode != StatusCodes.Status416RangeNotSatisfiable)
            {
                httpContext.Response.Headers.AcceptRanges = "bytes";
            }

            if (_body is null)
            {
                return;
            }

            httpContext.Response.ContentType = "application/json";
            await httpContext.Response.WriteAsync(_body, httpContext.RequestAborted);
        }
    }
}
