// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Downloads;

using Microsoft.Net.Http.Headers;
using ShadowDrop.Api.Shares;
using ShadowDrop.Api.Uploads;
using ShadowDrop.Contracts;
using ShadowDrop.Crypto;
using System.Security.Cryptography;
using System.Text.Json;

public sealed class DownloadFileService
{
    private const Int32 AesGcmTagLength = 16;
    private static readonly Byte[] CliEncryptedPayloadMarker = "\"encryptedPayload\":\"\""u8.ToArray();
    private static readonly Byte[] CliEncryptedPayloadPrefix = "\"encryptedPayload\":\""u8.ToArray();
    private readonly IBlobStorage _blobStorage;
    private readonly IShareMetadataRepository _shareMetadataRepository;
    private readonly TimeProvider _timeProvider;
    private readonly IUploadedFileMetadataRepository _uploadedFileMetadataRepository;

    public DownloadFileService(IShareMetadataRepository shareMetadataRepository,
                               IUploadedFileMetadataRepository uploadedFileMetadataRepository,
                               IBlobStorage blobStorage,
                               TimeProvider timeProvider)
    {
        _shareMetadataRepository = shareMetadataRepository;
        _uploadedFileMetadataRepository = uploadedFileMetadataRepository;
        _blobStorage = blobStorage;
        _timeProvider = timeProvider;
    }

    public async Task<DownloadLookupResult> ResolveAsync(String shareToken,
                                                         Guid fileId,
                                                         String? authorizationBearerToken,
                                                         String? headerKeyMaterial,
                                                         String? queryKeyMaterial,
                                                         CancellationToken cancellationToken) =>
        await ResolveAsync(shareToken,
                           fileId,
                           authorizationBearerToken,
                           headerKeyMaterial,
                           queryKeyMaterial,
                           null,
                           null,
                           null,
                           cancellationToken);

    public async Task<DownloadLookupResult> ResolveAsync(String shareToken,
                                                         Guid fileId,
                                                         String? authorizationBearerToken,
                                                         String? headerKeyMaterial,
                                                         String? queryKeyMaterial,
                                                         Int64? plaintextStart,
                                                         Int64? plaintextEndExclusive,
                                                         CancellationToken cancellationToken) =>
        await ResolveAsync(shareToken,
                           fileId,
                           authorizationBearerToken,
                           headerKeyMaterial,
                           queryKeyMaterial,
                           null,
                           plaintextStart,
                           plaintextEndExclusive,
                           cancellationToken);

    public async Task<DownloadLookupResult> ResolveAsync(String shareToken,
                                                         Guid fileId,
                                                         String? authorizationBearerToken,
                                                         String? headerKeyMaterial,
                                                         String? queryKeyMaterial,
                                                         String? rangeHeader,
                                                         Int64? plaintextStart,
                                                         Int64? plaintextEndExclusive,
                                                         CancellationToken cancellationToken)
    {
        if (String.IsNullOrWhiteSpace(shareToken))
        {
            return new(DownloadLookupStatus.InvalidShare);
        }

        var share = await _shareMetadataRepository.GetByShareTokenHashAsync(TokenHashing.ComputeHashBase64(shareToken), cancellationToken);
        if (share is null || share.RevokedAtUtc is not null)
        {
            return new(DownloadLookupStatus.InvalidShare);
        }

        var now = _timeProvider.GetUtcNow();
        if (share.ExpiresAtUtc < now)
        {
            return new(DownloadLookupStatus.ExpiredShare);
        }

        if (share.DownloadBearerToken is not null)
        {
            if (String.IsNullOrWhiteSpace(authorizationBearerToken))
            {
                return new(DownloadLookupStatus.Forbidden);
            }

            if (share.DownloadBearerToken.ExpiresAtUtc < now
                || !TokenHashing.MatchesStoredHash(authorizationBearerToken, share.DownloadBearerToken.TokenHashBase64))
            {
                return new(DownloadLookupStatus.Forbidden);
            }
        }

        var fileEntry = share.Files.SingleOrDefault(file => file.FileId == fileId);
        if (fileEntry is null)
        {
            return new(DownloadLookupStatus.NotFound);
        }

        var uploadedFile = await _uploadedFileMetadataRepository.GetAsync(fileId, cancellationToken);
        if (uploadedFile is null)
        {
            return new(DownloadLookupStatus.NotFound);
        }

        var rangeResolution = ResolveRequestedRange(uploadedFile, rangeHeader, plaintextStart, plaintextEndExclusive);
        if (rangeResolution.Status != DownloadLookupStatus.Success)
        {
            return new(rangeResolution.Status);
        }

        var mode = share.DirectHttpEnabled ? DownloadMode.DirectHttp : DownloadMode.CliDecrypt;
        if (mode == DownloadMode.DirectHttp)
        {
            var hasHeaderKey = !String.IsNullOrWhiteSpace(headerKeyMaterial);
            var hasQueryKey = !String.IsNullOrWhiteSpace(queryKeyMaterial);
            if (hasHeaderKey == hasQueryKey)
            {
                return new(DownloadLookupStatus.InvalidRequest);
            }

            var presentedKeyMaterial = !String.IsNullOrWhiteSpace(headerKeyMaterial) ? headerKeyMaterial : queryKeyMaterial;
            var directHttpOpenResult = await TryOpenDirectHttpContentAsync(uploadedFile,
                                                                           presentedKeyMaterial!,
                                                                           rangeResolution.RequestedRange,
                                                                           cancellationToken);
            if (directHttpOpenResult.Status != DownloadLookupStatus.Success || directHttpOpenResult.Content is null)
            {
                return new(directHttpOpenResult.Status);
            }

            return new(DownloadLookupStatus.Success,
                       new(mode,
                           share.ShareId,
                           fileId,
                           fileEntry.DisplayName ?? fileEntry.OriginalFileName,
                           uploadedFile.ContentType ?? "application/octet-stream",
                           uploadedFile.ContentType ?? "application/octet-stream",
                           rangeResolution.RequestedRange?.End - rangeResolution.RequestedRange?.Start ?? uploadedFile.PlaintextLength,
                           directHttpOpenResult.Content,
                           uploadedFile.PlaintextLength,
                           rangeResolution.RequestedRange));
        }

        var cliRequestedRange = rangeResolution.RequestedRange ?? new RequestedPlaintextRangeContract
        {
            Start = 0,
            End = uploadedFile.PlaintextLength
        };
        var cliDecryptOpenResult = await TryOpenCliDecryptContentAsync(uploadedFile,
                                                                       cliRequestedRange,
                                                                       cancellationToken);
        if (cliDecryptOpenResult.Status != DownloadLookupStatus.Success || cliDecryptOpenResult.Content is null)
        {
            return new(cliDecryptOpenResult.Status);
        }

        return new(DownloadLookupStatus.Success,
                   new(mode,
                       share.ShareId,
                       fileId,
                       fileEntry.DisplayName ?? fileEntry.OriginalFileName,
                       uploadedFile.ContentType ?? "application/octet-stream",
                       "application/json",
                       cliDecryptOpenResult.ContentLength,
                       cliDecryptOpenResult.Content,
                       uploadedFile.PlaintextLength,
                       rangeResolution.IsPartial ? cliRequestedRange : null));
    }

    internal static async Task<T> WithDecodedDirectHttpKeyMaterialAsync<T>(String keyMaterial,
                                                                           Func<Byte[], Task<(T Result, Boolean OwnershipTransferred)>> action)
    {
        Byte[]? secretBytes = null;
        var ownershipTransferred = false;
        try
        {
            secretBytes = Convert.FromBase64String(keyMaterial);
            var actionResult = await action(secretBytes);
            ownershipTransferred = actionResult.OwnershipTransferred;
            return actionResult.Result;
        }
        finally
        {
            if (!ownershipTransferred && secretBytes is not null)
            {
                CryptographicOperations.ZeroMemory(secretBytes);
            }
        }
    }

    private static async Task<CliDecryptJsonStream> CreateCliDecryptJsonStreamAsync(Stream encryptedContent,
                                                                                    UploadedFileRecord uploadedFile,
                                                                                    CliResumableDownloadContract contract,
                                                                                    ChunkRange chunkRange,
                                                                                    CancellationToken cancellationToken)
    {
        var encryptedOffset = GetEncryptedOffsetForChunkIndex(uploadedFile, chunkRange.FirstChunkIndex);
        var encryptedLength = GetEncryptedLengthForChunkSpan(uploadedFile,
                                                             chunkRange.FirstChunkIndex,
                                                             chunkRange.LastChunkIndex);
        await SkipAsync(encryptedContent, encryptedOffset, cancellationToken);

        var template = JsonSerializer.SerializeToUtf8Bytes(contract, ContractsJsonSerializerContext.Default.CliResumableDownloadContract);
        var payloadMarkerIndex = FindSubsequence(template, CliEncryptedPayloadMarker);
        if (payloadMarkerIndex < 0)
        {
            throw new InvalidOperationException("The CLI resumable-download contract template is missing the encrypted payload marker.");
        }

        var payloadPrefixLength = payloadMarkerIndex + CliEncryptedPayloadPrefix.Length;
        return new(template, payloadPrefixLength, encryptedLength, encryptedContent);
    }

    private static Int32 FindSubsequence(Byte[] haystack, Byte[] needle)
    {
        if (needle.Length == 0)
        {
            return 0;
        }

        for (var startIndex = 0; startIndex <= haystack.Length - needle.Length; startIndex++)
        {
            if (haystack.AsSpan(startIndex, needle.Length).SequenceEqual(needle))
            {
                return startIndex;
            }
        }

        return -1;
    }

    private static Int64 GetEncryptedLengthForChunkSpan(UploadedFileRecord uploadedFile, Int64 firstChunkIndex, Int64 lastChunkIndex)
    {
        var endOffset = GetEncryptedOffsetForChunkIndex(uploadedFile, lastChunkIndex + 1);
        var startOffset = GetEncryptedOffsetForChunkIndex(uploadedFile, firstChunkIndex);
        return checked(endOffset - startOffset);
    }

    private static Int64 GetEncryptedOffsetForChunkIndex(UploadedFileRecord uploadedFile, Int64 chunkIndex)
    {
        if (chunkIndex <= 0)
        {
            return 0;
        }

        var fullSizedChunkCount = Math.Min(chunkIndex, Math.Max(uploadedFile.ChunkCount - 1, 0));
        var offset = checked(fullSizedChunkCount * (uploadedFile.ChunkSize + AesGcmTagLength));
        if (chunkIndex == uploadedFile.ChunkCount)
        {
            return checked(offset + GetFinalChunkPlaintextLength(uploadedFile) + AesGcmTagLength);
        }

        return offset;
    }

    private static Int32 GetFinalChunkPlaintextLength(UploadedFileRecord uploadedFile)
    {
        if (uploadedFile.ChunkCount <= 0 || uploadedFile.PlaintextLength <= 0)
        {
            return 0;
        }

        if (uploadedFile.ChunkSize <= 0)
        {
            throw new InvalidDataException("Chunked metadata must declare a positive chunk size.");
        }

        var fullChunksLength = checked((uploadedFile.ChunkCount - 1) * (Int64)uploadedFile.ChunkSize);
        var remainingPlaintextLength = checked(uploadedFile.PlaintextLength - fullChunksLength);
        var finalChunkLength = remainingPlaintextLength == 0
            ? uploadedFile.ChunkSize
            : checked((Int32)remainingPlaintextLength);
        if (finalChunkLength < 1 || finalChunkLength > uploadedFile.ChunkSize)
        {
            throw new InvalidDataException("Chunked metadata produced an invalid final chunk length.");
        }

        return finalChunkLength;
    }

    private static Int64 GetPlaintextLengthForChunkIndex(UploadedFileRecord uploadedFile, Int64 chunkIndex)
    {
        if (chunkIndex == uploadedFile.ChunkCount - 1)
        {
            return GetFinalChunkPlaintextLength(uploadedFile);
        }

        return uploadedFile.ChunkSize;
    }

    private static Int64 GetPlaintextLengthForChunkSpan(UploadedFileRecord uploadedFile,
                                                        Int64 firstChunkIndex,
                                                        Int64 lastChunkIndex)
    {
        if (firstChunkIndex > lastChunkIndex)
        {
            return 0;
        }

        var chunkCount = lastChunkIndex - firstChunkIndex + 1;
        if (lastChunkIndex == uploadedFile.ChunkCount - 1)
        {
            return checked(((chunkCount - 1) * uploadedFile.ChunkSize) + GetFinalChunkPlaintextLength(uploadedFile));
        }

        return checked(chunkCount * uploadedFile.ChunkSize);
    }

    private static RangeResolution ResolveHeaderRange(Int64 totalPlaintextLength, String rangeHeader)
    {
        if (!RangeHeaderValue.TryParse(rangeHeader, out var parsedRange)
            || !String.Equals(parsedRange.Unit.ToString(), "bytes", StringComparison.OrdinalIgnoreCase)
            || parsedRange.Ranges.Count != 1)
        {
            return new(DownloadLookupStatus.InvalidRange, null, false);
        }

        var range = parsedRange.Ranges.Single();
        if (range.From is null && range.To is null)
        {
            return new(DownloadLookupStatus.InvalidRange, null, false);
        }

        Int64 start;
        Int64 endExclusive;
        if (range.From is null)
        {
            if (range.To is null || range.To <= 0)
            {
                return new(DownloadLookupStatus.InvalidRange, null, false);
            }

            var suffixLength = Math.Min(range.To.Value, totalPlaintextLength);
            if (suffixLength == 0)
            {
                return new(DownloadLookupStatus.RangeNotSatisfiable, null, false);
            }

            start = totalPlaintextLength - suffixLength;
            endExclusive = totalPlaintextLength;
        }
        else
        {
            start = range.From.Value;
            if (start < 0)
            {
                return new(DownloadLookupStatus.InvalidRange, null, false);
            }

            if (start >= totalPlaintextLength)
            {
                return new(DownloadLookupStatus.RangeNotSatisfiable, null, false);
            }

            if (range.To is not null && range.To < range.From)
            {
                return new(DownloadLookupStatus.InvalidRange, null, false);
            }

            var endInclusive = range.To is null || range.To.Value >= totalPlaintextLength
                ? totalPlaintextLength - 1
                : range.To.Value;
            endExclusive = endInclusive + 1;
        }

        return new(DownloadLookupStatus.Success,
                   new()
                   {
                       Start = start,
                       End = endExclusive
                   },
                   true);
    }

    private static RangeResolution ResolveRequestedRange(UploadedFileRecord uploadedFile,
                                                         String? rangeHeader,
                                                         Int64? plaintextStart,
                                                         Int64? plaintextEndExclusive)
    {
        var hasQueryRange = plaintextStart is not null || plaintextEndExclusive is not null;
        var hasHeaderRange = !String.IsNullOrWhiteSpace(rangeHeader);
        if (hasHeaderRange && hasQueryRange)
        {
            return new(DownloadLookupStatus.InvalidRange, null, false);
        }

        if (hasHeaderRange)
        {
            return ResolveHeaderRange(uploadedFile.PlaintextLength, rangeHeader!);
        }

        if (!hasQueryRange)
        {
            return new(DownloadLookupStatus.Success, null, false);
        }

        if (plaintextStart == Int64.MinValue || plaintextEndExclusive == Int64.MinValue)
        {
            return new(DownloadLookupStatus.InvalidRequest, null, false);
        }

        if (plaintextStart is null || plaintextEndExclusive is null)
        {
            return new(DownloadLookupStatus.InvalidRequest, null, false);
        }

        if (plaintextStart < 0 || plaintextEndExclusive <= plaintextStart)
        {
            return new(DownloadLookupStatus.InvalidRequest, null, false);
        }

        if (plaintextStart >= uploadedFile.PlaintextLength || plaintextEndExclusive > uploadedFile.PlaintextLength)
        {
            return new(DownloadLookupStatus.RangeNotSatisfiable, null, false);
        }

        return new(DownloadLookupStatus.Success,
                   new()
                   {
                       Start = plaintextStart.Value,
                       End = plaintextEndExclusive.Value
                   },
                   true);
    }

    private static async Task SkipAsync(Stream stream, Int64 bytesToSkip, CancellationToken cancellationToken)
    {
        if (bytesToSkip == 0)
        {
            return;
        }

        if (stream.CanSeek)
        {
            stream.Seek(bytesToSkip, SeekOrigin.Begin);
            return;
        }

        var remaining = bytesToSkip;
        var buffer = new Byte[8192];
        while (remaining > 0)
        {
            var bytesRead = await stream.ReadAsync(buffer.AsMemory(0, (Int32)Math.Min(buffer.Length, remaining)),
                                                   cancellationToken);
            if (bytesRead == 0)
            {
                throw new EndOfStreamException("Unexpected end of encrypted content while skipping to a chunk span.");
            }

            remaining -= bytesRead;
        }
    }

    private async Task<CliDecryptOpenResult> TryOpenCliDecryptContentAsync(UploadedFileRecord uploadedFile,
                                                                           RequestedPlaintextRangeContract requestedRange,
                                                                           CancellationToken cancellationToken)
    {
        Stream? encryptedContent = null;

        try
        {
            var chunkRange = ChunkEncryptionService.GetChunkRange(requestedRange.Start,
                                                                  requestedRange.End - requestedRange.Start,
                                                                  uploadedFile.ChunkSize);

            encryptedContent = await TryOpenEncryptedContentAsync(uploadedFile.BlobKey, cancellationToken);
            if (encryptedContent is null)
            {
                return new(DownloadLookupStatus.NotFound, null, 0);
            }

            var contract = new CliResumableDownloadContract
            {
                FirstChunkIndex = chunkRange.FirstChunkIndex,
                LastChunkIndex = chunkRange.LastChunkIndex,
                EncryptedPayload = String.Empty,
                RequestedRange = requestedRange,
                TotalPlaintextSize = uploadedFile.PlaintextLength,
                ChunkSize = uploadedFile.ChunkSize,
                FinalChunkPlaintextLength = GetFinalChunkPlaintextLength(uploadedFile)
            };

            var content = await CreateCliDecryptJsonStreamAsync(encryptedContent,
                                                                uploadedFile,
                                                                contract,
                                                                chunkRange,
                                                                cancellationToken);
            encryptedContent = null;
            return new(DownloadLookupStatus.Success, content, content.Length);
        }
        catch (Exception exception) when (exception is ArgumentException
                                                       or ArgumentOutOfRangeException
                                                       or EndOfStreamException
                                                       or IOException
                                                       or InvalidDataException
                                                       or OverflowException)
        {
            if (encryptedContent is not null)
            {
                await encryptedContent.DisposeAsync();
            }

            return new(DownloadLookupStatus.InvalidRequest, null, 0);
        }
    }

    private async Task<DirectHttpOpenResult> TryOpenDirectHttpContentAsync(UploadedFileRecord uploadedFile,
                                                                           String keyMaterial,
                                                                           RequestedPlaintextRangeContract? requestedRange,
                                                                           CancellationToken cancellationToken)
    {
        Stream? encryptedContent = null;
        try
        {
            return await WithDecodedDirectHttpKeyMaterialAsync(
                keyMaterial,
                async secretBytes =>
                {
                    encryptedContent = await TryOpenEncryptedContentAsync(uploadedFile.BlobKey, cancellationToken);
                    if (encryptedContent is null)
                    {
                        return (new(DownloadLookupStatus.NotFound, null), false);
                    }

                    var decryptedContent = await DirectHttpDecryptingStream.CreateAsync(encryptedContent,
                                                                                        uploadedFile,
                                                                                        secretBytes,
                                                                                        requestedRange,
                                                                                        cancellationToken);
                    return (new DirectHttpOpenResult(DownloadLookupStatus.Success, decryptedContent), true);
                });
        }
        catch (Exception exception) when (exception is ArgumentException
                                                       or CryptographicException
                                                       or EndOfStreamException
                                                       or FormatException
                                                       or InvalidDataException
                                                       or IOException
                                                       or OverflowException)
        {
            if (encryptedContent is not null)
            {
                await encryptedContent.DisposeAsync();
            }

            return new(DownloadLookupStatus.InvalidRequest, null);
        }
    }

    private async Task<Stream?> TryOpenEncryptedContentAsync(String blobKey, CancellationToken cancellationToken)
    {
        try
        {
            return await _blobStorage.OpenReadAsync(blobKey, cancellationToken);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
    }

    private sealed class Base64EncodingStream(Stream source, Int64 sourceLength) : Stream
    {
        private const Int32 InputBufferSize = 12 * 1024;
        private readonly ToBase64Transform _base64Transform = new();
        private readonly Byte[] _carryBuffer = new Byte[2];
        private readonly Byte[] _inputBuffer = new Byte[InputBufferSize];
        private readonly Stream _source = source;
        private readonly Int64 _sourceLength = sourceLength;
        private Boolean _disposed;
        private Byte[] _encodedBuffer = [];
        private Int32 _encodedBufferLength;
        private Int32 _encodedBufferOffset;
        private Boolean _hasCompletedTransform;
        private Int32 _pendingCarryCount;
        private Int64 _remainingSourceBytes = sourceLength;

        public override Boolean CanRead => !_disposed;
        public override Boolean CanSeek => false;
        public override Boolean CanWrite => false;
        public override Int64 Length => GetEncodedLength(_sourceLength);

        public override Int64 Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public static Int64 GetEncodedLength(Int64 sourceLength) => checked((sourceLength + 2) / 3 * 4);

        public override async ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            CryptographicOperations.ZeroMemory(_carryBuffer);
            CryptographicOperations.ZeroMemory(_inputBuffer);
            CryptographicOperations.ZeroMemory(_encodedBuffer);
            _base64Transform.Dispose();
            await _source.DisposeAsync();
            await base.DisposeAsync();
        }

        public override void Flush() => throw new NotSupportedException();

        public override Int32 Read(Byte[] buffer, Int32 offset, Int32 count) =>
            ReadAsync(buffer.AsMemory(offset, count), CancellationToken.None).AsTask().GetAwaiter().GetResult();

        public override Task<Int32> ReadAsync(Byte[] buffer, Int32 offset, Int32 count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override async ValueTask<Int32> ReadAsync(Memory<Byte> buffer, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (buffer.IsEmpty)
            {
                return 0;
            }

            var totalBytesRead = 0;
            while (totalBytesRead < buffer.Length)
            {
                if (_encodedBufferOffset < _encodedBufferLength)
                {
                    var bytesToCopy = Math.Min(buffer.Length - totalBytesRead, _encodedBufferLength - _encodedBufferOffset);
                    _encodedBuffer.AsSpan(_encodedBufferOffset, bytesToCopy).CopyTo(buffer.Span[totalBytesRead..]);
                    _encodedBufferOffset += bytesToCopy;
                    totalBytesRead += bytesToCopy;
                    continue;
                }

                if (_hasCompletedTransform)
                {
                    return totalBytesRead;
                }

                await FillEncodedBufferAsync(cancellationToken);
            }

            return totalBytesRead;
        }

        public override Int64 Seek(Int64 offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(Int64 value) => throw new NotSupportedException();
        public override void Write(Byte[] buffer, Int32 offset, Int32 count) => throw new NotSupportedException();

        protected override void Dispose(Boolean disposing)
        {
            if (disposing && !_disposed)
            {
                _disposed = true;
                CryptographicOperations.ZeroMemory(_carryBuffer);
                CryptographicOperations.ZeroMemory(_inputBuffer);
                CryptographicOperations.ZeroMemory(_encodedBuffer);
                _base64Transform.Dispose();
                _source.Dispose();
            }

            base.Dispose(disposing);
        }

        private void EnsureEncodedBufferCapacity(Int32 requiredLength)
        {
            if (_encodedBuffer.Length >= requiredLength)
            {
                return;
            }

            if (_encodedBuffer.Length > 0)
            {
                CryptographicOperations.ZeroMemory(_encodedBuffer);
            }

            _encodedBuffer = new Byte[requiredLength];
        }

        private async Task FillEncodedBufferAsync(CancellationToken cancellationToken)
        {
            while (true)
            {
                if (_remainingSourceBytes == 0)
                {
                    _encodedBuffer = _pendingCarryCount == 0
                        ? []
                        : _base64Transform.TransformFinalBlock(_carryBuffer, 0, _pendingCarryCount);
                    _encodedBufferLength = _encodedBuffer.Length;
                    _encodedBufferOffset = 0;
                    _pendingCarryCount = 0;
                    _hasCompletedTransform = true;
                    return;
                }

                if (_pendingCarryCount > 0)
                {
                    _carryBuffer.AsSpan(0, _pendingCarryCount).CopyTo(_inputBuffer);
                }

                var bytesToRead = (Int32)Math.Min(_inputBuffer.Length - _pendingCarryCount, _remainingSourceBytes);
                var bytesRead = await _source.ReadAsync(_inputBuffer.AsMemory(_pendingCarryCount, bytesToRead), cancellationToken);
                if (bytesRead == 0)
                {
                    throw new EndOfStreamException("Unexpected end of encrypted content while Base64-encoding the CLI response payload.");
                }

                _remainingSourceBytes -= bytesRead;

                var totalInputBytes = _pendingCarryCount + bytesRead;
                var fullBlockByteCount = totalInputBytes / 3 * 3;
                var remainingBytes = totalInputBytes - fullBlockByteCount;

                if (fullBlockByteCount == 0)
                {
                    _inputBuffer.AsSpan(0, remainingBytes).CopyTo(_carryBuffer);
                    _pendingCarryCount = remainingBytes;
                    continue;
                }

                var encodedLength = _base64Transform.OutputBlockSize * (fullBlockByteCount / _base64Transform.InputBlockSize);
                EnsureEncodedBufferCapacity(encodedLength);
                _encodedBufferLength = _base64Transform.TransformBlock(_inputBuffer, 0, fullBlockByteCount, _encodedBuffer, 0);
                _encodedBufferOffset = 0;

                if (remainingBytes > 0)
                {
                    _inputBuffer.AsSpan(fullBlockByteCount, remainingBytes).CopyTo(_carryBuffer);
                }

                _pendingCarryCount = remainingBytes;
                return;
            }
        }
    }

    private sealed class CliDecryptJsonStream : Stream
    {
        private readonly Base64EncodingStream _base64PayloadStream;
        private readonly Int64 _length;
        private readonly Int32 _payloadSuffixOffset;
        private readonly Byte[] _template;
        private Boolean _disposed;
        private StreamPhase _phase;
        private Int32 _templateOffset;

        public CliDecryptJsonStream(Byte[] template, Int32 payloadPrefixLength, Int64 encryptedPayloadLength, Stream encryptedContent)
        {
            _template = template;
            _payloadSuffixOffset = payloadPrefixLength;
            _base64PayloadStream = new(encryptedContent, encryptedPayloadLength);
            _length = checked(payloadPrefixLength
                              + Base64EncodingStream.GetEncodedLength(encryptedPayloadLength)
                              + (template.LongLength - payloadPrefixLength));
        }

        public override Boolean CanRead => !_disposed;
        public override Boolean CanSeek => false;
        public override Boolean CanWrite => false;
        public override Int64 Length => _length;

        public override Int64 Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override async ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            await _base64PayloadStream.DisposeAsync();
            await base.DisposeAsync();
        }

        public override void Flush() => throw new NotSupportedException();

        public override Int32 Read(Byte[] buffer, Int32 offset, Int32 count) =>
            ReadAsync(buffer.AsMemory(offset, count), CancellationToken.None).AsTask().GetAwaiter().GetResult();

        public override Task<Int32> ReadAsync(Byte[] buffer, Int32 offset, Int32 count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override async ValueTask<Int32> ReadAsync(Memory<Byte> buffer, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (buffer.IsEmpty)
            {
                return 0;
            }

            var totalBytesRead = 0;
            while (totalBytesRead < buffer.Length)
            {
                switch (_phase)
                {
                    case StreamPhase.TemplatePrefix:
                        totalBytesRead += CopyTemplateBytes(buffer.Span[totalBytesRead..], _payloadSuffixOffset);
                        if (_templateOffset >= _payloadSuffixOffset)
                        {
                            _phase = StreamPhase.Base64Payload;
                        }

                        break;
                    case StreamPhase.Base64Payload:
                        var payloadBytesRead = await _base64PayloadStream.ReadAsync(buffer[totalBytesRead..], cancellationToken);
                        totalBytesRead += payloadBytesRead;
                        if (payloadBytesRead == 0)
                        {
                            _phase = StreamPhase.TemplateSuffix;
                        }

                        break;
                    case StreamPhase.TemplateSuffix:
                        totalBytesRead += CopyTemplateBytes(buffer.Span[totalBytesRead..], _template.Length);
                        if (_templateOffset >= _template.Length)
                        {
                            _phase = StreamPhase.Completed;
                        }

                        break;
                    case StreamPhase.Completed:
                        return totalBytesRead;
                    default:
                        throw new ArgumentOutOfRangeException();
                }

                if (totalBytesRead == 0)
                {
                    return 0;
                }
            }

            return totalBytesRead;
        }

        public override Int64 Seek(Int64 offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(Int64 value) => throw new NotSupportedException();
        public override void Write(Byte[] buffer, Int32 offset, Int32 count) => throw new NotSupportedException();

        protected override void Dispose(Boolean disposing)
        {
            if (disposing && !_disposed)
            {
                _disposed = true;
                _base64PayloadStream.Dispose();
            }

            base.Dispose(disposing);
        }

        private Int32 CopyTemplateBytes(Span<Byte> destination, Int32 endExclusive)
        {
            var remainingTemplateBytes = endExclusive - _templateOffset;
            if (remainingTemplateBytes <= 0)
            {
                return 0;
            }

            var bytesToCopy = Math.Min(destination.Length, remainingTemplateBytes);
            _template.AsSpan(_templateOffset, bytesToCopy).CopyTo(destination);
            _templateOffset += bytesToCopy;
            return bytesToCopy;
        }

        private enum StreamPhase
        {
            TemplatePrefix,
            Base64Payload,
            TemplateSuffix,
            Completed
        }
    }

    private sealed record CliDecryptOpenResult(DownloadLookupStatus Status, Stream? Content, Int64 ContentLength);

    private sealed class DirectHttpDecryptingStream : Stream
    {
        private readonly ContentKey _contentKey;
        private readonly Stream _encryptedContent;
        private readonly Byte[] _kdfSalt;
        private readonly Int64 _lastChunkIndex;
        private readonly Int64 _responseLength;
        private readonly Byte[] _shareSecret;
        private readonly UploadedFileRecord _uploadedFile;
        private Byte[] _currentChunk = [];
        private Int32 _currentChunkOffset;
        private Boolean _disposed;
        private Int64 _nextChunkIndex;
        private Int64 _remainingResponseLength;
        private Int64 _remainingSpanPlaintextLength;

        private DirectHttpDecryptingStream(Stream encryptedContent,
                                           ContentKey contentKey,
                                           Byte[] kdfSalt,
                                           UploadedFileRecord uploadedFile,
                                           Byte[] shareSecret,
                                           Int64 nextChunkIndex,
                                           Int64 lastChunkIndex,
                                           Int64 responseLength,
                                           Int64 remainingSpanPlaintextLength)
        {
            _encryptedContent = encryptedContent;
            _contentKey = contentKey;
            _kdfSalt = kdfSalt;
            _uploadedFile = uploadedFile;
            _shareSecret = shareSecret;
            _nextChunkIndex = nextChunkIndex;
            _lastChunkIndex = lastChunkIndex;
            _responseLength = responseLength;
            _remainingResponseLength = responseLength;
            _remainingSpanPlaintextLength = remainingSpanPlaintextLength;
        }

        public override Boolean CanRead => !_disposed;
        public override Boolean CanSeek => false;
        public override Boolean CanWrite => false;
        public override Int64 Length => _responseLength;

        public override Int64 Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public static async Task<DirectHttpDecryptingStream> CreateAsync(Stream encryptedContent,
                                                                         UploadedFileRecord uploadedFile,
                                                                         Byte[] shareSecret,
                                                                         CancellationToken cancellationToken) =>
            await CreateAsync(encryptedContent, uploadedFile, shareSecret, null, cancellationToken);

        public static async Task<DirectHttpDecryptingStream> CreateAsync(Stream encryptedContent,
                                                                         UploadedFileRecord uploadedFile,
                                                                         Byte[] shareSecret,
                                                                         RequestedPlaintextRangeContract? requestedRange,
                                                                         CancellationToken cancellationToken)
        {
            Byte[]? kdfSalt = null;
            ContentKey? contentKey = null;
            DirectHttpDecryptingStream? stream = null;
            try
            {
                kdfSalt = Convert.FromBase64String(uploadedFile.KdfSaltBase64);
                using var derivedShareSecret = ShareSecret.FromBytes(shareSecret);
                var context = new FileEncryptionContext(uploadedFile.FileId, kdfSalt);
                contentKey = ChunkEncryptionService.DeriveContentKey(derivedShareSecret, context);
                var range = requestedRange ?? new RequestedPlaintextRangeContract
                {
                    Start = 0,
                    End = uploadedFile.PlaintextLength
                };
                var chunkRange = ChunkEncryptionService.GetChunkRange(range.Start,
                                                                      range.End - range.Start,
                                                                      uploadedFile.ChunkSize);
                var encryptedOffset = GetEncryptedOffsetForChunkIndex(uploadedFile, chunkRange.FirstChunkIndex);
                var remainingSpanPlaintextLength = GetPlaintextLengthForChunkSpan(uploadedFile,
                                                                                  chunkRange.FirstChunkIndex,
                                                                                  chunkRange.LastChunkIndex);

                await SkipAsync(encryptedContent, encryptedOffset, cancellationToken);
                stream = new(encryptedContent,
                             contentKey,
                             kdfSalt,
                             uploadedFile,
                             shareSecret,
                             chunkRange.FirstChunkIndex,
                             chunkRange.LastChunkIndex,
                             range.End - range.Start,
                             remainingSpanPlaintextLength);
                contentKey = null;
                if (uploadedFile.ChunkCount > 0)
                {
                    await stream.LoadNextChunkAsync(cancellationToken);
                    stream._currentChunkOffset = chunkRange.OffsetInFirstChunk;
                }
                else if (stream._encryptedContent.ReadByte() != -1)
                {
                    throw new EndOfStreamException("Encrypted stream contained unexpected trailing data.");
                }

                return stream;
            }
            catch
            {
                if (stream is not null)
                {
                    await stream.DisposeAsync();
                }
                else
                {
                    if (kdfSalt is not null)
                    {
                        CryptographicOperations.ZeroMemory(kdfSalt);
                    }

                    contentKey?.Dispose();
                    CryptographicOperations.ZeroMemory(shareSecret);
                    await encryptedContent.DisposeAsync();
                }

                throw;
            }
        }

        public override async ValueTask DisposeAsync()
        {
            if (!TryDisposeCore())
            {
                return;
            }

            await _encryptedContent.DisposeAsync();
            await base.DisposeAsync();
        }

        public override void Flush() => throw new NotSupportedException();

        public override Int32 Read(Byte[] buffer, Int32 offset, Int32 count) =>
            ReadAsync(buffer.AsMemory(offset, count), CancellationToken.None).AsTask().GetAwaiter().GetResult();

        public override Int32 Read(Span<Byte> buffer)
        {
            var destination = new Byte[buffer.Length];
            var bytesRead = Read(destination, 0, destination.Length);
            destination.AsSpan(0, bytesRead).CopyTo(buffer);
            return bytesRead;
        }

        public override async Task<Int32> ReadAsync(Byte[] buffer, Int32 offset, Int32 count, CancellationToken cancellationToken) =>
            await ReadAsync(buffer.AsMemory(offset, count), cancellationToken);

        public override async ValueTask<Int32> ReadAsync(Memory<Byte> buffer, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (buffer.Length == 0 || _remainingResponseLength == 0)
            {
                return 0;
            }

            var bytesRead = 0;
            while (bytesRead < buffer.Length && _remainingResponseLength > 0)
            {
                if (_currentChunkOffset >= _currentChunk.Length)
                {
                    if (_nextChunkIndex > _lastChunkIndex)
                    {
                        break;
                    }

                    await LoadNextChunkAsync(cancellationToken);
                }

                var bytesToCopy = (Int32)Math.Min(Math.Min(buffer.Length - bytesRead, _currentChunk.Length - _currentChunkOffset),
                                                  _remainingResponseLength);
                _currentChunk.AsSpan(_currentChunkOffset, bytesToCopy).CopyTo(buffer.Span[bytesRead..]);
                _currentChunkOffset += bytesToCopy;
                bytesRead += bytesToCopy;
                _remainingResponseLength -= bytesToCopy;
            }

            return bytesRead;
        }

        public override Int64 Seek(Int64 offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(Int64 value) => throw new NotSupportedException();
        public override void Write(Byte[] buffer, Int32 offset, Int32 count) => throw new NotSupportedException();

        protected override void Dispose(Boolean disposing)
        {
            if (disposing && TryDisposeCore())
            {
                _encryptedContent.Dispose();
            }

            base.Dispose(disposing);
        }

        private async Task LoadNextChunkAsync(CancellationToken cancellationToken)
        {
            var plaintextChunkLength = (Int32)GetPlaintextLengthForChunkIndex(_uploadedFile, _nextChunkIndex);
            var encryptedChunkLength = checked(plaintextChunkLength + AesGcmTagLength);
            var encryptedChunkBytes = new Byte[encryptedChunkLength];
            await _encryptedContent.ReadExactlyAsync(encryptedChunkBytes, cancellationToken);

            var metadata = new ChunkMetadata(CryptoVersion.V1,
                                             CryptoAlgorithm.Aes256Gcm,
                                             _uploadedFile.FileId,
                                             _uploadedFile.ChunkSize,
                                             _nextChunkIndex,
                                             plaintextChunkLength);
            _currentChunk = ChunkEncryptionService.DecryptChunk(new(encryptedChunkBytes), _contentKey, metadata);
            _currentChunkOffset = 0;
            _remainingSpanPlaintextLength -= plaintextChunkLength;
            _nextChunkIndex++;

            if (_nextChunkIndex == _uploadedFile.ChunkCount)
            {
                var trailingByte = _encryptedContent.ReadByte();
                if (_remainingSpanPlaintextLength != 0 || trailingByte != -1)
                {
                    throw new EndOfStreamException("Encrypted stream length did not match metadata.");
                }
            }
        }

        private Boolean TryDisposeCore()
        {
            if (_disposed)
            {
                return false;
            }

            _disposed = true;
            _contentKey.Dispose();
            CryptographicOperations.ZeroMemory(_kdfSalt);
            CryptographicOperations.ZeroMemory(_shareSecret);
            CryptographicOperations.ZeroMemory(_currentChunk);
            _currentChunk = [];
            _currentChunkOffset = 0;
            return true;
        }
    }

    private sealed record DirectHttpOpenResult(DownloadLookupStatus Status, Stream? Content);

    private sealed record RangeResolution(DownloadLookupStatus Status, RequestedPlaintextRangeContract? RequestedRange, Boolean IsPartial);
}
