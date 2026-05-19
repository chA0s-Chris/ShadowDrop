// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Downloads;

using ShadowDrop.Contracts;
using System.Net.Http.Headers;

/// <summary>
/// Parses and validates streamed CLI download responses.
/// </summary>
public static class CliDownloadResponseParser
{
    private const Int32 AesGcmTagLength = 16;

    /// <summary>
    /// Parses and validates a streamed CLI download response.
    /// </summary>
    /// <param name="response">The HTTP response.</param>
    /// <param name="expectedRange">The optional plaintext range that was requested.</param>
    /// <returns>The validated response wrapper.</returns>
    public static CliDownloadResponse Parse(HttpResponseMessage response, RequestedPlaintextRangeContract? expectedRange = null)
    {
        ArgumentNullException.ThrowIfNull(response);

        ValidateContentType(response.Content.Headers.ContentType);

        var metadata = new CliDownloadMetadataContract
        {
            FirstChunkIndex = ReadRequiredInt64Header(response, DownloadHeaderConstants.FirstChunkIndexHeaderName),
            LastChunkIndex = ReadRequiredInt64Header(response, DownloadHeaderConstants.LastChunkIndexHeaderName),
            RequestedRange = new()
            {
                Start = ReadRequiredInt64Header(response, DownloadHeaderConstants.PlaintextRangeStartHeaderName),
                End = ReadRequiredInt64Header(response, DownloadHeaderConstants.PlaintextRangeEndHeaderName)
            },
            TotalPlaintextSize = ReadRequiredInt64Header(response, DownloadHeaderConstants.TotalPlaintextSizeHeaderName),
            ChunkSize = ReadRequiredInt32Header(response, DownloadHeaderConstants.ChunkSizeHeaderName),
            FinalChunkPlaintextLength = ReadRequiredInt32Header(response, DownloadHeaderConstants.FinalChunkPlaintextLengthHeaderName)
        };

        var expectedEncryptedLength = ValidateMetadata(metadata, expectedRange);
        var declaredContentLength = response.Content.Headers.ContentLength;
        if (declaredContentLength is not null && declaredContentLength.Value != expectedEncryptedLength)
        {
            throw new InvalidDataException("The streamed CLI download response declared an unexpected body length.");
        }

        return new(metadata, new LengthValidatingReadStream(response.Content.ReadAsStream(), expectedEncryptedLength));
    }

    private static Int64 GetChunkCount(CliDownloadMetadataContract metadata)
    {
        if (metadata.TotalPlaintextSize <= 0)
        {
            return 0;
        }

        return checked(((metadata.TotalPlaintextSize - 1) / metadata.ChunkSize) + 1);
    }

    private static Int64 GetExpectedEncryptedLength(CliDownloadMetadataContract metadata)
    {
        var chunkCount = checked(metadata.LastChunkIndex - metadata.FirstChunkIndex + 1);
        if (chunkCount == 0)
        {
            return 0;
        }

        var fullChunkCount = metadata.LastChunkIndex == GetChunkCount(metadata) - 1
            ? chunkCount - 1
            : chunkCount;
        var encryptedLength = checked(fullChunkCount * checked((Int64)metadata.ChunkSize + AesGcmTagLength));
        if (metadata.LastChunkIndex == GetChunkCount(metadata) - 1)
        {
            encryptedLength = checked(encryptedLength + metadata.FinalChunkPlaintextLength + AesGcmTagLength);
        }

        return encryptedLength;
    }

    private static Int32 ReadRequiredInt32Header(HttpResponseMessage response, String headerName)
    {
        var value = ReadRequiredInt64Header(response, headerName);
        try
        {
            return checked((Int32)value);
        }
        catch (OverflowException exception)
        {
            throw new InvalidDataException($"The streamed CLI download response contains an invalid {headerName} header.", exception);
        }
    }

    private static Int64 ReadRequiredInt64Header(HttpResponseMessage response, String headerName)
    {
        if (!response.Headers.TryGetValues(headerName, out var values))
        {
            throw new InvalidDataException($"The streamed CLI download response is missing the {headerName} header.");
        }

        var materializedValues = values.ToArray();
        if (materializedValues.Length != 1 || !Int64.TryParse(materializedValues[0], out var value))
        {
            throw new InvalidDataException($"The streamed CLI download response contains an invalid {headerName} header.");
        }

        return value;
    }

    private static void ValidateContentType(MediaTypeHeaderValue? contentType)
    {
        if (contentType is null
            || !String.Equals(contentType.MediaType, DownloadHeaderConstants.CliDownloadContentType, StringComparison.OrdinalIgnoreCase)
            || contentType.Parameters.Count > 0)
        {
            throw new InvalidDataException("The streamed CLI download response uses an unsupported media type.");
        }
    }

    private static Int64 ValidateMetadata(CliDownloadMetadataContract metadata, RequestedPlaintextRangeContract? expectedRange)
    {
        if (metadata.ChunkSize <= 0)
        {
            throw new InvalidDataException("The streamed CLI download response contains an invalid chunk size.");
        }

        if (metadata.TotalPlaintextSize <= 0)
        {
            throw new InvalidDataException("The streamed CLI download response contains an invalid total plaintext size.");
        }

        if (metadata.FinalChunkPlaintextLength <= 0 || metadata.FinalChunkPlaintextLength > metadata.ChunkSize)
        {
            throw new InvalidDataException("The streamed CLI download response contains an invalid final chunk length.");
        }

        if (metadata.FirstChunkIndex < 0 || metadata.LastChunkIndex < metadata.FirstChunkIndex)
        {
            throw new InvalidDataException("The streamed CLI download response contains an invalid chunk span.");
        }

        if (metadata.RequestedRange.Start < 0
            || metadata.RequestedRange.End <= metadata.RequestedRange.Start
            || metadata.RequestedRange.End > metadata.TotalPlaintextSize)
        {
            throw new InvalidDataException("The streamed CLI download response contains an invalid plaintext range.");
        }

        var chunkCount = GetChunkCount(metadata);
        if (chunkCount <= 0 || metadata.LastChunkIndex >= chunkCount)
        {
            throw new InvalidDataException("The streamed CLI download response contains an out-of-bounds chunk span.");
        }

        var firstChunkStart = checked(metadata.FirstChunkIndex * (Int64)metadata.ChunkSize);
        var lastChunkPlaintextLength = metadata.LastChunkIndex == chunkCount - 1
            ? metadata.FinalChunkPlaintextLength
            : metadata.ChunkSize;
        var lastChunkEnd = checked((metadata.LastChunkIndex * (Int64)metadata.ChunkSize) + lastChunkPlaintextLength);
        if (metadata.RequestedRange.Start < firstChunkStart || metadata.RequestedRange.End > lastChunkEnd)
        {
            throw new InvalidDataException("The streamed CLI download response contains inconsistent plaintext and chunk metadata.");
        }

        if (expectedRange is not null
            && (expectedRange.Start != metadata.RequestedRange.Start || expectedRange.End != metadata.RequestedRange.End))
        {
            throw new InvalidDataException("The streamed CLI download response does not match the requested plaintext range.");
        }

        return GetExpectedEncryptedLength(metadata);
    }

    private sealed class LengthValidatingReadStream : Stream
    {
        private readonly Int64 _expectedLength;
        private readonly Stream _inner;
        private Int64 _remainingLength;
        private Boolean _validatedTrailingData;

        public LengthValidatingReadStream(Stream inner, Int64 expectedLength)
        {
            _inner = inner;
            _expectedLength = expectedLength;
            _remainingLength = expectedLength;
        }

        public override Boolean CanRead => _inner.CanRead;
        public override Boolean CanSeek => false;
        public override Boolean CanWrite => false;
        public override Int64 Length => _expectedLength;

        public override Int64 Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override ValueTask DisposeAsync() => _inner.DisposeAsync();
        public override void Flush() => throw new NotSupportedException();

        public override Int32 Read(Byte[] buffer, Int32 offset, Int32 count) =>
            ReadAsync(buffer.AsMemory(offset, count), CancellationToken.None).AsTask().GetAwaiter().GetResult();

        public override Task<Int32> ReadAsync(Byte[] buffer, Int32 offset, Int32 count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override async ValueTask<Int32> ReadAsync(Memory<Byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_remainingLength == 0)
            {
                await EnsureNoTrailingDataAsync(cancellationToken);
                return 0;
            }

            var bytesRead = await _inner.ReadAsync(buffer[..(Int32)Math.Min(buffer.Length, _remainingLength)], cancellationToken);
            if (bytesRead == 0)
            {
                throw new InvalidDataException("The streamed CLI download response body ended before the advertised chunk span was fully received.");
            }

            _remainingLength -= bytesRead;
            return bytesRead;
        }

        public override Int64 Seek(Int64 offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(Int64 value) => throw new NotSupportedException();
        public override void Write(Byte[] buffer, Int32 offset, Int32 count) => throw new NotSupportedException();

        private async Task EnsureNoTrailingDataAsync(CancellationToken cancellationToken)
        {
            if (_validatedTrailingData)
            {
                return;
            }

            _validatedTrailingData = true;
            var trailingBuffer = new Byte[1];
            if (await _inner.ReadAsync(trailingBuffer, cancellationToken) != 0)
            {
                throw new InvalidDataException("The streamed CLI download response body exceeded the advertised chunk span.");
            }
        }
    }
}
