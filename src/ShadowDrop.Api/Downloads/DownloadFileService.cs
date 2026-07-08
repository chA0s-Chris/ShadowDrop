// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Downloads;

using Microsoft.Net.Http.Headers;
using ShadowDrop.Api.Shares;
using ShadowDrop.Api.Uploads;
using ShadowDrop.Contracts;
using ShadowDrop.Crypto;
using System.Security.Cryptography;

public sealed class DownloadFileService
{
    private const Int32 AesGcmTagLength = 16;
    private readonly IBlobStorage _blobStorage;
    private readonly ILogger<DownloadFileService> _logger;
    private readonly IShareMetadataRepository _shareMetadataRepository;
    private readonly TimeProvider _timeProvider;
    private readonly IUploadedFileMetadataRepository _uploadedFileMetadataRepository;

    public DownloadFileService(IShareMetadataRepository shareMetadataRepository,
                               IUploadedFileMetadataRepository uploadedFileMetadataRepository,
                               IBlobStorage blobStorage,
                               TimeProvider timeProvider,
                               ILogger<DownloadFileService> logger)
    {
        _shareMetadataRepository = shareMetadataRepository;
        _uploadedFileMetadataRepository = uploadedFileMetadataRepository;
        _blobStorage = blobStorage;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<DownloadLookupResult> ResolveAsync(DownloadRequest request, CancellationToken cancellationToken)
    {
        var shareResolution = await TryResolveShareAsync(request.ShareToken, request.AuthorizationBearerToken, cancellationToken);
        if (shareResolution.Status != DownloadLookupStatus.Success || shareResolution.Context is null)
        {
            return new(shareResolution.Status);
        }

        var share = shareResolution.Context.Share;

        var fileEntry = share.Files.SingleOrDefault(file => file.FileId == request.FileId);
        if (fileEntry is null)
        {
            _logger.LogWarning("Download request rejected because the file is not part of the share. ShareId: {ShareId}; FileId: {FileId}",
                               share.ShareId,
                               request.FileId);
            return new(DownloadLookupStatus.NotFound);
        }

        var uploadedFile = await _uploadedFileMetadataRepository.GetAsync(request.FileId, cancellationToken);
        if (uploadedFile is null)
        {
            _logger.LogWarning("Download request rejected because upload metadata was missing. ShareId: {ShareId}; FileId: {FileId}",
                               share.ShareId,
                               request.FileId);
            return new(DownloadLookupStatus.NotFound);
        }

        if (request.Mode == DownloadRequestMode.DirectHttp)
        {
            if (!share.DirectHttpEnabled)
            {
                _logger.LogWarning("Download request rejected because direct-HTTP mode is not enabled for the share. ShareId: {ShareId}; FileId: {FileId}",
                                   share.ShareId,
                                   request.FileId);
                return new(DownloadLookupStatus.InvalidRequest);
            }

            if (request.HasMalformedRangeHeader)
            {
                _logger.LogWarning("Download request rejected because the range header was malformed. ShareId: {ShareId}; FileId: {FileId}",
                                   share.ShareId,
                                   request.FileId);
                return new(DownloadLookupStatus.InvalidRange);
            }

            var rangeResolution = ResolveDirectHttpRequestedRange(uploadedFile.PlaintextLength, request.RequestedRange);
            if (rangeResolution.Status != DownloadLookupStatus.Success)
            {
                _logger.LogWarning(
                    "Download request rejected because the requested range was invalid. ShareId: {ShareId}; FileId: {FileId}; Status: {Status}",
                    share.ShareId,
                    request.FileId,
                    rangeResolution.Status);
                return new(rangeResolution.Status);
            }

            var hasHeaderKey = !String.IsNullOrWhiteSpace(request.HeaderKeyMaterial);
            var hasQueryKey = !String.IsNullOrWhiteSpace(request.QueryKeyMaterial);
            if (hasHeaderKey == hasQueryKey)
            {
                _logger.LogWarning(
                    "Download request rejected because key material was missing or provided in both the header and query string. ShareId: {ShareId}; FileId: {FileId}",
                    share.ShareId,
                    request.FileId);
                return new(DownloadLookupStatus.InvalidRequest);
            }

            var presentedKeyMaterial = hasHeaderKey ? request.HeaderKeyMaterial : request.QueryKeyMaterial;
            var directHttpOpenResult = await TryOpenDirectHttpContentAsync(uploadedFile,
                                                                           presentedKeyMaterial!,
                                                                           rangeResolution.RequestedRange,
                                                                           cancellationToken);
            if (directHttpOpenResult.Status != DownloadLookupStatus.Success || directHttpOpenResult.Content is null)
            {
                return new(directHttpOpenResult.Status);
            }

            return new(DownloadLookupStatus.Success,
                       new(DownloadMode.DirectHttp,
                           share.ShareId,
                           request.FileId,
                           fileEntry.DisplayName ?? fileEntry.OriginalFileName,
                           uploadedFile.ContentType ?? "application/octet-stream",
                           uploadedFile.ContentType ?? "application/octet-stream",
                           rangeResolution.RequestedRange?.End - rangeResolution.RequestedRange?.Start ?? uploadedFile.PlaintextLength,
                           directHttpOpenResult.Content,
                           uploadedFile.PlaintextLength,
                           rangeResolution.RequestedRange,
                           null));
        }

        if (share.DirectHttpEnabled)
        {
            _logger.LogWarning("Download request rejected because streamed CLI mode is not enabled for the share. ShareId: {ShareId}; FileId: {FileId}",
                               share.ShareId,
                               request.FileId);
            return new(DownloadLookupStatus.InvalidRequest);
        }

        if (request.HasMalformedRangeHeader)
        {
            _logger.LogWarning("Download request rejected because the range header was malformed. ShareId: {ShareId}; FileId: {FileId}",
                               share.ShareId,
                               request.FileId);
            return new(DownloadLookupStatus.InvalidRange);
        }

        var cliRangeResolution = ResolveCliRequestedRange(uploadedFile.PlaintextLength, request.RequestedRange);
        if (cliRangeResolution.Status != DownloadLookupStatus.Success || cliRangeResolution.RequestedRange is null)
        {
            _logger.LogWarning(
                "Download request rejected because the requested range was invalid. ShareId: {ShareId}; FileId: {FileId}; Status: {Status}",
                share.ShareId,
                request.FileId,
                cliRangeResolution.Status);
            return new(cliRangeResolution.Status);
        }

        var cliOpenResult = await TryOpenCliContentAsync(uploadedFile,
                                                         cliRangeResolution.RequestedRange,
                                                         cancellationToken);
        if (cliOpenResult.Status != DownloadLookupStatus.Success || cliOpenResult.Content is null || cliOpenResult.Metadata is null)
        {
            return new(cliOpenResult.Status);
        }

        return new(DownloadLookupStatus.Success,
                   new(DownloadMode.Cli,
                       share.ShareId,
                       request.FileId,
                       fileEntry.DisplayName ?? fileEntry.OriginalFileName,
                       uploadedFile.ContentType ?? "application/octet-stream",
                       DownloadHeaderConstants.CliDownloadContentType,
                       cliOpenResult.ContentLength,
                       cliOpenResult.Content,
                       uploadedFile.PlaintextLength,
                       cliRangeResolution.IsPartial ? cliRangeResolution.RequestedRange : null,
                       cliOpenResult.Metadata));
    }

    public async Task<ShareManifestLookupResult> ResolveManifestAsync(String shareToken,
                                                                      String? authorizationBearerToken,
                                                                      CancellationToken cancellationToken)
    {
        var shareResolution = await TryResolveShareAsync(shareToken, authorizationBearerToken, cancellationToken);
        if (shareResolution.Status != DownloadLookupStatus.Success || shareResolution.Context is null)
        {
            return new(shareResolution.Status);
        }

        var files = new List<ShareManifestFileContract>(shareResolution.Context.Share.Files.Count);
        foreach (var file in shareResolution.Context.Share.Files)
        {
            var uploadedFile = await _uploadedFileMetadataRepository.GetAsync(file.FileId, cancellationToken);
            if (uploadedFile is null)
            {
                _logger.LogWarning(
                    "Share manifest request rejected because upload metadata was missing. ShareId: {ShareId}; FileId: {FileId}",
                    shareResolution.Context.Share.ShareId,
                    file.FileId);
                return new(DownloadLookupStatus.NotFound);
            }

            files.Add(new()
            {
                FileId = file.FileId.ToString(),
                FileName = file.DisplayName ?? file.OriginalFileName,
                Length = uploadedFile.PlaintextLength,
                EncryptionFormatVersion = uploadedFile.EncryptionFormatVersion,
                AlgorithmId = uploadedFile.AlgorithmId,
                ChunkSize = uploadedFile.ChunkSize,
                ChunkCount = uploadedFile.ChunkCount,
                KdfSalt = uploadedFile.KdfSaltBase64,
                PlaintextSha256 = uploadedFile.PlaintextSha256
            });
        }

        return new(DownloadLookupStatus.Success, new()
        {
            Files = files
        });
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

        // Valid callers pass either an existing chunk index or ChunkCount as the exclusive end boundary.
        // Therefore the cap only protects the end-boundary case; indexes between ChunkCount - 1 and
        // ChunkCount cannot exist because chunk indexes are integral.
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

    private static ParsedRangeHeader ParseRequestedByteRange(String rangeHeader)
    {
        if (!RangeHeaderValue.TryParse(rangeHeader, out var parsedRange)
            || !String.Equals(parsedRange.Unit.ToString(), "bytes", StringComparison.OrdinalIgnoreCase)
            || parsedRange.Ranges.Count != 1)
        {
            return new(null, true);
        }

        var range = parsedRange.Ranges.Single();
        if (range.From is null && range.To is null)
        {
            return new(null, true);
        }

        return new(new(range.From, range.To), false);
    }

    private static RangeResolution ResolveCliRequestedRange(Int64 totalPlaintextLength, RequestedByteRange? requestedRange)
    {
        if (requestedRange is null)
        {
            return new(DownloadLookupStatus.Success,
                       new()
                       {
                           Start = 0,
                           End = totalPlaintextLength
                       },
                       false);
        }

        if (requestedRange.Start is null || requestedRange.EndInclusive is null)
        {
            return new(DownloadLookupStatus.InvalidRange, null, false);
        }

        var start = requestedRange.Start.Value;
        var endInclusive = requestedRange.EndInclusive.Value;
        if (start < 0 || endInclusive < start)
        {
            return new(DownloadLookupStatus.InvalidRange, null, false);
        }

        if (start >= totalPlaintextLength)
        {
            return new(DownloadLookupStatus.RangeNotSatisfiable, null, false);
        }

        var endExclusive = endInclusive >= totalPlaintextLength
            ? totalPlaintextLength
            : checked(endInclusive + 1);
        return new(DownloadLookupStatus.Success,
                   new()
                   {
                       Start = start,
                       End = endExclusive
                   },
                   true);
    }

    private static RangeResolution ResolveDirectHttpRequestedRange(Int64 totalPlaintextLength, RequestedByteRange? requestedRange)
    {
        if (requestedRange is null)
        {
            return new(DownloadLookupStatus.Success, null, false);
        }

        Int64 start;
        Int64 endExclusive;
        if (requestedRange.Start is null)
        {
            if (requestedRange.EndInclusive is null || requestedRange.EndInclusive <= 0)
            {
                return new(DownloadLookupStatus.InvalidRange, null, false);
            }

            var suffixLength = Math.Min(requestedRange.EndInclusive.Value, totalPlaintextLength);
            if (suffixLength == 0)
            {
                return new(DownloadLookupStatus.RangeNotSatisfiable, null, false);
            }

            start = totalPlaintextLength - suffixLength;
            endExclusive = totalPlaintextLength;
        }
        else
        {
            start = requestedRange.Start.Value;
            if (start < 0)
            {
                return new(DownloadLookupStatus.InvalidRange, null, false);
            }

            if (start >= totalPlaintextLength)
            {
                return new(DownloadLookupStatus.RangeNotSatisfiable, null, false);
            }

            if (requestedRange.EndInclusive is not null && requestedRange.EndInclusive < start)
            {
                return new(DownloadLookupStatus.InvalidRange, null, false);
            }

            var endInclusive = requestedRange.EndInclusive is null || requestedRange.EndInclusive.Value >= totalPlaintextLength
                ? totalPlaintextLength - 1
                : requestedRange.EndInclusive.Value;
            endExclusive = checked(endInclusive + 1);
        }

        return new(DownloadLookupStatus.Success,
                   new()
                   {
                       Start = start,
                       End = endExclusive
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

    private async Task<CliOpenResult> TryOpenCliContentAsync(UploadedFileRecord uploadedFile,
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
                _logger.LogWarning("Download content missing from blob storage. FileId: {FileId}; BlobKey: {BlobKey}",
                                   uploadedFile.FileId,
                                   uploadedFile.BlobKey);
                return new(DownloadLookupStatus.NotFound, null, 0, null);
            }

            var encryptedOffset = GetEncryptedOffsetForChunkIndex(uploadedFile, chunkRange.FirstChunkIndex);
            var encryptedLength = GetEncryptedLengthForChunkSpan(uploadedFile, chunkRange.FirstChunkIndex, chunkRange.LastChunkIndex);
            await SkipAsync(encryptedContent, encryptedOffset, cancellationToken);

            var metadata = new CliDownloadMetadataContract
            {
                FirstChunkIndex = chunkRange.FirstChunkIndex,
                LastChunkIndex = chunkRange.LastChunkIndex,
                RequestedRange = requestedRange,
                TotalPlaintextSize = uploadedFile.PlaintextLength,
                ChunkSize = uploadedFile.ChunkSize,
                FinalChunkPlaintextLength = GetFinalChunkPlaintextLength(uploadedFile)
            };

            var content = new LengthLimitingReadStream(encryptedContent, encryptedLength);
            encryptedContent = null;
            return new(DownloadLookupStatus.Success,
                       content,
                       encryptedLength,
                       metadata);
        }
        catch (Exception exception) when (exception is ArgumentException
                                                       or ArgumentOutOfRangeException
                                                       or EndOfStreamException
                                                       or IOException
                                                       or InvalidDataException
                                                       or OverflowException)
        {
            _logger.LogWarning(exception,
                               "Download request rejected because CLI content resolution failed. FileId: {FileId}; BlobKey: {BlobKey}",
                               uploadedFile.FileId,
                               uploadedFile.BlobKey);

            if (encryptedContent is not null)
            {
                await encryptedContent.DisposeAsync();
            }

            return new(DownloadLookupStatus.InvalidRequest, null, 0, null);
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
                        _logger.LogWarning("Download content missing from blob storage. FileId: {FileId}; BlobKey: {BlobKey}",
                                           uploadedFile.FileId,
                                           uploadedFile.BlobKey);
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
            _logger.LogWarning(exception,
                               "Download request rejected because direct-HTTP content resolution failed. FileId: {FileId}; BlobKey: {BlobKey}",
                               uploadedFile.FileId,
                               uploadedFile.BlobKey);

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

    private async Task<ShareResolution> TryResolveShareAsync(String shareToken, String? authorizationBearerToken, CancellationToken cancellationToken)
    {
        if (String.IsNullOrWhiteSpace(shareToken))
        {
            _logger.LogWarning("Download request rejected because no share token was provided");
            return new(DownloadLookupStatus.InvalidShare, null);
        }

        var share = await _shareMetadataRepository.GetByShareTokenHashAsync(TokenHashing.ComputeHashBase64(shareToken), cancellationToken);
        if (share is null || share.RevokedAtUtc is not null)
        {
            _logger.LogWarning("Download request rejected because the share is unknown or revoked. ShareId: {ShareId}", share?.ShareId);
            return new(DownloadLookupStatus.InvalidShare, null);
        }

        var now = _timeProvider.GetUtcNow();
        if (share.ExpiresAtUtc < now)
        {
            _logger.LogWarning("Download request rejected because the share has expired. ShareId: {ShareId}", share.ShareId);
            return new(DownloadLookupStatus.ExpiredShare, null);
        }

        if (share.DownloadBearerToken is not null)
        {
            if (String.IsNullOrWhiteSpace(authorizationBearerToken))
            {
                _logger.LogWarning(
                    "Download request rejected because a bearer token was required but not provided. ShareId: {ShareId}",
                    share.ShareId);
                return new(DownloadLookupStatus.Forbidden, null);
            }

            if (share.DownloadBearerToken.ExpiresAtUtc < now
                || !TokenHashing.MatchesStoredHash(authorizationBearerToken, share.DownloadBearerToken.TokenHashBase64))
            {
                _logger.LogWarning(
                    "Download request rejected because the bearer token was invalid or expired. ShareId: {ShareId}",
                    share.ShareId);
                return new(DownloadLookupStatus.Forbidden, null);
            }
        }

        return new(DownloadLookupStatus.Success, new(share, now));
    }

    private sealed record CliOpenResult(DownloadLookupStatus Status, Stream? Content, Int64 ContentLength, CliDownloadMetadataContract? Metadata);

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
                                             plaintextChunkLength,
                                             _nextChunkIndex == _uploadedFile.ChunkCount - 1);
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

    private sealed class LengthLimitingReadStream(Stream source, Int64 sourceLength) : Stream
    {
        private readonly Stream _source = source;
        private readonly Int64 _sourceLength = sourceLength;
        private Boolean _disposed;
        private Int64 _remainingSourceBytes = sourceLength;

        public override Boolean CanRead => !_disposed;
        public override Boolean CanSeek => false;
        public override Boolean CanWrite => false;
        public override Int64 Length => _sourceLength;

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

            if (buffer.IsEmpty || _remainingSourceBytes == 0)
            {
                return 0;
            }

            var bytesToRead = (Int32)Math.Min(buffer.Length, _remainingSourceBytes);
            var bytesRead = await _source.ReadAsync(buffer[..bytesToRead], cancellationToken);
            if (bytesRead == 0)
            {
                throw new EndOfStreamException("Unexpected end of encrypted content while streaming the CLI response payload.");
            }

            _remainingSourceBytes -= bytesRead;
            return bytesRead;
        }

        public override Int64 Seek(Int64 offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(Int64 value) => throw new NotSupportedException();
        public override void Write(Byte[] buffer, Int32 offset, Int32 count) => throw new NotSupportedException();

        protected override void Dispose(Boolean disposing)
        {
            if (disposing && !_disposed)
            {
                _disposed = true;
                _source.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    private sealed record ParsedRangeHeader(RequestedByteRange? Range, Boolean IsMalformed);

    private sealed record RangeResolution(DownloadLookupStatus Status, RequestedPlaintextRangeContract? RequestedRange, Boolean IsPartial);

    private sealed record ResolvedShareContext(ShareRecord Share, DateTimeOffset Now);

    private sealed record ShareResolution(DownloadLookupStatus Status, ResolvedShareContext? Context);
}
