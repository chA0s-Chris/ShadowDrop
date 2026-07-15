// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Uploads;

using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;
using ShadowDrop.Contracts;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

internal static partial class MultipartUploadRequestReader
{
    private const Int32 AesGcmTagLength = 16;
    private const Int32 DefaultMaxMetadataBytes = 64 * 1024;

    // Fallback for the convenience overload only; the running application passes the configured ShadowDrop:Upload:MaxBytes value.
    private const Int64 DefaultMaxUploadBodyBytes = 4L * 1024 * 1024 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static async Task<(UploadPersistenceRequest Request, Stream EncryptedContent)> ReadAsync(HttpRequest httpRequest,
                                                                                                    CancellationToken cancellationToken)
        => await ReadAsync(httpRequest, cancellationToken, DefaultMaxUploadBodyBytes);

    internal static async Task<(UploadPersistenceRequest Request, Stream EncryptedContent)> ReadAsync(HttpRequest httpRequest,
                                                                                                      CancellationToken cancellationToken,
                                                                                                      Int64 maxUploadBodyBytes,
                                                                                                      Int32 maxMetadataBytes = DefaultMaxMetadataBytes,
                                                                                                      Int64? maxEncryptedFileBytes = null)
    {
        ArgumentNullException.ThrowIfNull(httpRequest);

        if (!MediaTypeHeaderValue.TryParse(httpRequest.ContentType, out var contentTypeHeader)
            || !String.Equals(contentTypeHeader.MediaType.Value, "multipart/form-data", StringComparison.OrdinalIgnoreCase))
        {
            throw new UploadValidationException("Uploads must use multipart/form-data.");
        }

        if (httpRequest.ContentLength.HasValue && httpRequest.ContentLength.Value > maxUploadBodyBytes)
        {
            throw new UploadPayloadTooLargeException();
        }

        var boundary = HeaderUtilities.RemoveQuotes(contentTypeHeader.Boundary).Value;
        if (String.IsNullOrWhiteSpace(boundary))
        {
            throw new UploadValidationException("Uploads must declare a multipart boundary.");
        }

        var reader = new MultipartReader(boundary, new MaxLengthReadStream(httpRequest.Body, maxUploadBodyBytes));

        var metadataSection = await reader.ReadNextSectionAsync(cancellationToken)
                              ?? throw new UploadValidationException("The metadata section is required.");
        ValidateSectionName(metadataSection, "metadata");
        ValidateSectionContentType(metadataSection.ContentType, "application/json", "The metadata section must use application/json.");
        EnsureSectionHasNoFileName(metadataSection, "metadata");

        var metadataJson = await ReadMetadataJsonAsync(metadataSection.Body, maxMetadataBytes, cancellationToken);
        MultipartUploadRequest metadataRequest;
        try
        {
            metadataRequest = JsonSerializer.Deserialize<MultipartUploadRequest>(metadataJson, JsonOptions)
                              ?? throw new UploadValidationException("The metadata section is required.");
        }
        catch (JsonException)
        {
            throw new UploadValidationException("The metadata section is invalid.");
        }

        var request = Validate(metadataRequest);
        var effectiveMaxEncryptedFileBytes = maxEncryptedFileBytes
                                             ?? maxUploadBodyBytes;
        if (request.EncryptedLength > effectiveMaxEncryptedFileBytes)
        {
            throw new UploadPayloadTooLargeException();
        }

        var contentSection = await reader.ReadNextSectionAsync(cancellationToken)
                             ?? throw new UploadValidationException("The encrypted content section is required.");
        ValidateSectionName(contentSection, "content");
        ValidateSectionContentType(contentSection.ContentType,
                                   "application/octet-stream",
                                   "The encrypted content section must use application/octet-stream.");

        return (request,
                new ValidatingMultipartContentStream(contentSection.Body,
                                                     request.EncryptedLength,
                                                     async completionToken =>
                                                     {
                                                         var trailingSection = await reader.ReadNextSectionAsync(completionToken);
                                                         if (trailingSection is not null)
                                                         {
                                                             throw new UploadValidationException(
                                                                 "Uploads may only include metadata and encrypted content sections.");
                                                         }
                                                     }));
    }

    private static void EnsureSectionHasNoFileName(MultipartSection section, String expectedName)
    {
        if (!ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var contentDisposition))
        {
            throw new UploadValidationException($"The {expectedName} section must include a valid Content-Disposition header.");
        }

        if (contentDisposition.FileName.HasValue
            || contentDisposition.FileNameStar.HasValue)
        {
            throw new UploadValidationException($"The {expectedName} section must not be sent as a file part.");
        }
    }

    [GeneratedRegex("^[0-9a-f]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex PlaintextSha256Regex();

    private static async Task<String> ReadMetadataJsonAsync(Stream body, Int32 maxMetadataBytes, CancellationToken cancellationToken)
    {
        using var memoryStream = new MemoryStream();
        var buffer = new Byte[8192];
        var totalBytesRead = 0;
        while (true)
        {
            var bytesRead = await body.ReadAsync(buffer, cancellationToken);
            if (bytesRead == 0)
            {
                break;
            }

            totalBytesRead = checked(totalBytesRead + bytesRead);
            if (totalBytesRead > maxMetadataBytes)
            {
                throw new UploadValidationException("The metadata section is too large.");
            }

            await memoryStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
        }

        if (memoryStream.Length == 0)
        {
            throw new UploadValidationException("The metadata section is required.");
        }

        return Encoding.UTF8.GetString(memoryStream.GetBuffer(), 0, checked((Int32)memoryStream.Length));
    }

    private static UploadPersistenceRequest Validate(MultipartUploadRequest request)
    {
        if (request.FileId == Guid.Empty)
        {
            throw new UploadValidationException("The reserved file id is required.");
        }

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

        Int64 expectedChunkCount;
        Int64 expectedEncryptedLength;
        try
        {
            expectedChunkCount = checked(((request.PlaintextLength - 1) / request.ChunkSize) + 1);
            expectedEncryptedLength = checked(request.PlaintextLength + (request.ChunkCount * AesGcmTagLength));
        }
        catch (OverflowException)
        {
            throw new UploadValidationException("Upload metadata is internally inconsistent.");
        }

        if (request.ChunkCount != expectedChunkCount)
        {
            throw new UploadValidationException("Chunk metadata is internally inconsistent.");
        }

        if (request.EncryptedLength != expectedEncryptedLength)
        {
            throw new UploadValidationException("Encrypted length metadata is internally inconsistent.");
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

        return new(request.FileId,
                   request.OriginalFileName,
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

    private static void ValidateSectionContentType(String? contentType, String expectedContentType, String message)
    {
        if (!MediaTypeHeaderValue.TryParse(contentType, out var header)
            || !String.Equals(header.MediaType.Value, expectedContentType, StringComparison.OrdinalIgnoreCase))
        {
            throw new UploadValidationException(message);
        }
    }

    private static void ValidateSectionName(MultipartSection section, String expectedName)
    {
        if (!ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var contentDisposition)
            || !String.Equals(contentDisposition.Name.Value, expectedName, StringComparison.Ordinal))
        {
            throw new UploadValidationException($"The multipart request must include a '{expectedName}' section.");
        }
    }

    private sealed class MaxLengthReadStream : Stream
    {
        private readonly Stream _innerStream;
        private readonly Int64 _maxLength;
        private Int64 _totalBytesRead;

        public MaxLengthReadStream(Stream innerStream, Int64 maxLength)
        {
            _innerStream = innerStream;
            _maxLength = maxLength;
        }

        public override Boolean CanRead => _innerStream.CanRead;

        public override Boolean CanSeek => false;

        public override Boolean CanWrite => false;

        public override Int64 Length => throw new NotSupportedException();

        public override Int64 Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => throw new NotSupportedException();

        public override Int32 Read(Byte[] buffer, Int32 offset, Int32 count) =>
            ReadAsync(buffer.AsMemory(offset, count), CancellationToken.None).GetAwaiter().GetResult();

        public override async ValueTask<Int32> ReadAsync(Memory<Byte> buffer, CancellationToken cancellationToken = default)
        {
            var bytesRead = await _innerStream.ReadAsync(buffer, cancellationToken);
            if (bytesRead == 0)
            {
                return 0;
            }

            _totalBytesRead = checked(_totalBytesRead + bytesRead);
            if (_totalBytesRead > _maxLength)
            {
                throw new UploadPayloadTooLargeException();
            }

            return bytesRead;
        }

        public override Task<Int32> ReadAsync(Byte[] buffer, Int32 offset, Int32 count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override Int64 Seek(Int64 offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(Int64 value) => throw new NotSupportedException();

        public override void Write(Byte[] buffer, Int32 offset, Int32 count) => throw new NotSupportedException();
    }

    private sealed class ValidatingMultipartContentStream : Stream
    {
        private readonly Func<CancellationToken, Task> _completionValidator;
        private readonly Int64 _declaredLength;
        private readonly Stream _innerStream;
        private Boolean _validated;
        private Int64 _writtenLength;

        public ValidatingMultipartContentStream(Stream innerStream, Int64 declaredLength, Func<CancellationToken, Task> completionValidator)
        {
            _innerStream = innerStream;
            _declaredLength = declaredLength;
            _completionValidator = completionValidator;
        }

        public override Boolean CanRead => _innerStream.CanRead;

        public override Boolean CanSeek => false;

        public override Boolean CanWrite => false;

        public override Int64 Length => throw new NotSupportedException();

        public override Int64 Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => throw new NotSupportedException();

        public override Int32 Read(Byte[] buffer, Int32 offset, Int32 count) =>
            ReadAsync(buffer.AsMemory(offset, count), CancellationToken.None).GetAwaiter().GetResult();

        public override async ValueTask<Int32> ReadAsync(Memory<Byte> buffer, CancellationToken cancellationToken = default)
        {
            var bytesRead = await _innerStream.ReadAsync(buffer, cancellationToken);
            if (bytesRead > 0)
            {
                _writtenLength = checked(_writtenLength + bytesRead);
                if (_writtenLength > _declaredLength)
                {
                    throw new UploadValidationException("The encrypted content length does not match the declared metadata.");
                }

                return bytesRead;
            }

            await EnsureValidatedAsync(cancellationToken);
            return 0;
        }

        public override Task<Int32> ReadAsync(Byte[] buffer, Int32 offset, Int32 count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override Int64 Seek(Int64 offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(Int64 value) => throw new NotSupportedException();

        public override void Write(Byte[] buffer, Int32 offset, Int32 count) => throw new NotSupportedException();

        private async Task EnsureValidatedAsync(CancellationToken cancellationToken)
        {
            if (_validated)
            {
                return;
            }

            _validated = true;
            if (_writtenLength != _declaredLength)
            {
                throw new UploadValidationException("The encrypted content length does not match the declared metadata.");
            }

            await _completionValidator(cancellationToken);
        }
    }
}
