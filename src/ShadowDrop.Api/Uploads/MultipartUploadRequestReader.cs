// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Uploads;

using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;
using ShadowDrop.Contracts;
using System.Text.Json;
using System.Text.RegularExpressions;

internal static partial class MultipartUploadRequestReader
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task<(UploadPersistenceRequest Request, Stream EncryptedContent)> ReadAsync(HttpRequest httpRequest,
                                                                                                    CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpRequest);

        if (!MediaTypeHeaderValue.TryParse(httpRequest.ContentType, out var contentTypeHeader)
            || !String.Equals(contentTypeHeader.MediaType.Value, "multipart/form-data", StringComparison.OrdinalIgnoreCase))
        {
            throw new UploadValidationException("Uploads must use multipart/form-data.");
        }

        var form = await httpRequest.ReadFormAsync(cancellationToken);
        if (!form.TryGetValue("metadata", out var metadataJson) || StringValues.IsNullOrEmpty(metadataJson))
        {
            throw new UploadValidationException("The metadata section is required.");
        }

        var metadataRequest = JsonSerializer.Deserialize<MultipartUploadRequest>(metadataJson[0]!, JsonOptions)
                              ?? throw new UploadValidationException("The metadata section is required.");
        var request = Validate(metadataRequest);

        var contentFile = form.Files.GetFile("content");
        if (contentFile is null)
        {
            throw new UploadValidationException("The encrypted content section is required.");
        }

        if (!String.IsNullOrWhiteSpace(contentFile.ContentType)
            && !String.Equals(contentFile.ContentType, "application/octet-stream", StringComparison.OrdinalIgnoreCase))
        {
            throw new UploadValidationException("The encrypted content section must use application/octet-stream.");
        }

        return (request, contentFile.OpenReadStream());
    }

    [GeneratedRegex("^[0-9a-f]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex PlaintextSha256Regex();

    private static UploadPersistenceRequest Validate(MultipartUploadRequest request)
    {
        if (String.IsNullOrWhiteSpace(request.OriginalFileName))
        {
            throw new UploadValidationException("The original file name is required.");
        }

        if (request.PlaintextLength < 0 || request.EncryptedLength < 0)
        {
            throw new UploadValidationException("File lengths must be zero or greater.");
        }

        if (request.ChunkSize <= 0 || request.ChunkCount <= 0)
        {
            throw new UploadValidationException("Chunk metadata must be positive.");
        }

        if (request.PlaintextLength == 0 || request.EncryptedLength == 0)
        {
            throw new UploadValidationException("Zero-length uploads are not supported.");
        }

        if (!String.Equals(request.EncryptionFormatVersion, FormatConstants.EncryptionFormatVersion, StringComparison.Ordinal)
            || !String.Equals(request.AlgorithmId, FormatConstants.Aes256GcmAlgorithmId, StringComparison.Ordinal))
        {
            throw new UploadValidationException("Unsupported encryption metadata was supplied.");
        }

        if (String.IsNullOrWhiteSpace(request.KdfSalt))
        {
            throw new UploadValidationException("The KDF salt is required.");
        }

        Byte[] saltBytes;
        try
        {
            saltBytes = Convert.FromBase64String(request.KdfSalt);
        }
        catch (FormatException)
        {
            throw new UploadValidationException("The KDF salt must be Base64 encoded.");
        }

        if (saltBytes.Length != 32)
        {
            throw new UploadValidationException("The KDF salt must be exactly 32 bytes.");
        }

        if (request.PlaintextSha256 is not null && !PlaintextSha256Regex().IsMatch(request.PlaintextSha256))
        {
            throw new UploadValidationException("The plaintext SHA-256 must be a lowercase hexadecimal digest.");
        }

        return new UploadPersistenceRequest(request.OriginalFileName,
                                            request.PlaintextLength,
                                            request.EncryptedLength,
                                            request.ContentType,
                                            request.EncryptionFormatVersion!,
                                            request.AlgorithmId!,
                                            request.ChunkSize,
                                            request.ChunkCount,
                                            request.KdfSalt,
                                            request.PlaintextSha256);
    }
}
