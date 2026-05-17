// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Downloads;

using ShadowDrop.Api.Shares;
using ShadowDrop.Api.Uploads;
using ShadowDrop.Crypto;
using System.Security.Cryptography;

public sealed class DownloadFileService
{
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

        var mode = share.DirectHttpEnabled ? DownloadMode.DirectHttp : DownloadMode.CliDecrypt;
        if (mode == DownloadMode.DirectHttp)
        {
            var hasHeaderKey = !String.IsNullOrWhiteSpace(headerKeyMaterial);
            var hasQueryKey = !String.IsNullOrWhiteSpace(queryKeyMaterial);
            if (hasHeaderKey == hasQueryKey)
            {
                return new(DownloadLookupStatus.InvalidRequest);
            }
        }

        if (mode == DownloadMode.DirectHttp)
        {
            var presentedKeyMaterial = !String.IsNullOrWhiteSpace(headerKeyMaterial) ? headerKeyMaterial : queryKeyMaterial;
            var directHttpOpenResult = await TryOpenDirectHttpContentAsync(uploadedFile,
                                                                           presentedKeyMaterial!,
                                                                           cancellationToken);
            if (directHttpOpenResult.Status == DownloadLookupStatus.NotFound)
            {
                return new(DownloadLookupStatus.NotFound);
            }

            if (directHttpOpenResult.Content is null)
            {
                return new(DownloadLookupStatus.InvalidRequest);
            }

            return new(DownloadLookupStatus.Success,
                       new(mode,
                           share.ShareId,
                           fileId,
                           fileEntry.DisplayName ?? fileEntry.OriginalFileName,
                           uploadedFile.ContentType ?? "application/octet-stream",
                           uploadedFile.PlaintextLength,
                           directHttpOpenResult.Content));
        }

        var encryptedContent = await TryOpenEncryptedContentAsync(uploadedFile.BlobKey, cancellationToken);
        if (encryptedContent is null)
        {
            return new(DownloadLookupStatus.NotFound);
        }

        return new(DownloadLookupStatus.Success,
                   new(mode,
                       share.ShareId,
                       fileId,
                       fileEntry.DisplayName ?? fileEntry.OriginalFileName,
                       uploadedFile.ContentType ?? "application/octet-stream",
                       uploadedFile.EncryptedLength,
                       encryptedContent));
    }

    internal static async Task<T> WithDecodedDirectHttpKeyMaterialAsync<T>(String keyMaterial, Func<Byte[], Task<T>> action)
    {
        Byte[]? secretBytes = null;
        var ownershipTransferred = false;
        try
        {
            secretBytes = Convert.FromBase64String(keyMaterial);
            var result = await action(secretBytes);
            ownershipTransferred = true;
            return result;
        }
        finally
        {
            if (!ownershipTransferred && secretBytes is not null)
            {
                CryptographicOperations.ZeroMemory(secretBytes);
            }
        }
    }

    private async Task<DirectHttpOpenResult> TryOpenDirectHttpContentAsync(UploadedFileRecord uploadedFile,
                                                                           String keyMaterial,
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
                        return new(DownloadLookupStatus.NotFound, null);
                    }

                    var decryptedContent = await DirectHttpDecryptingStream.CreateAsync(encryptedContent,
                                                                                        uploadedFile,
                                                                                        secretBytes,
                                                                                        cancellationToken);
                    return new DirectHttpOpenResult(DownloadLookupStatus.Success, decryptedContent);
                });
        }
        catch (Exception exception) when (exception is ArgumentException
                                                       or CryptographicException
                                                       or EndOfStreamException
                                                       or FormatException
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

    private sealed class DirectHttpDecryptingStream : Stream
    {
        private readonly ContentKey _contentKey;
        private readonly Stream _encryptedContent;
        private readonly Byte[] _kdfSalt;
        private readonly Byte[] _shareSecret;
        private readonly UploadedFileRecord _uploadedFile;
        private Byte[] _currentChunk = [];
        private Int32 _currentChunkOffset;
        private Boolean _disposed;
        private Int64 _nextChunkIndex;
        private Int64 _remainingPlaintextLength;

        private DirectHttpDecryptingStream(Stream encryptedContent,
                                           ContentKey contentKey,
                                           Byte[] kdfSalt,
                                           UploadedFileRecord uploadedFile,
                                           Byte[] shareSecret)
        {
            _encryptedContent = encryptedContent;
            _contentKey = contentKey;
            _kdfSalt = kdfSalt;
            _uploadedFile = uploadedFile;
            _shareSecret = shareSecret;
            _remainingPlaintextLength = uploadedFile.PlaintextLength;
        }

        public override Boolean CanRead => !_disposed;

        public override Boolean CanSeek => false;

        public override Boolean CanWrite => false;

        public override Int64 Length => _uploadedFile.PlaintextLength;

        public override Int64 Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public static async Task<DirectHttpDecryptingStream> CreateAsync(Stream encryptedContent,
                                                                         UploadedFileRecord uploadedFile,
                                                                         Byte[] shareSecret,
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
                stream = new(encryptedContent, contentKey, kdfSalt, uploadedFile, shareSecret);
                contentKey = null;
                if (uploadedFile.ChunkCount > 0)
                {
                    await stream.LoadNextChunkAsync(cancellationToken);
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
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _contentKey.Dispose();
            CryptographicOperations.ZeroMemory(_kdfSalt);
            CryptographicOperations.ZeroMemory(_shareSecret);
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

            if (buffer.Length == 0 || (_nextChunkIndex >= _uploadedFile.ChunkCount && _currentChunkOffset >= _currentChunk.Length))
            {
                return 0;
            }

            var bytesRead = 0;
            while (bytesRead < buffer.Length)
            {
                if (_currentChunkOffset >= _currentChunk.Length)
                {
                    if (_nextChunkIndex >= _uploadedFile.ChunkCount)
                    {
                        break;
                    }

                    await LoadNextChunkAsync(cancellationToken);
                }

                var bytesToCopy = Math.Min(buffer.Length - bytesRead, _currentChunk.Length - _currentChunkOffset);
                _currentChunk.AsSpan(_currentChunkOffset, bytesToCopy).CopyTo(buffer.Span[bytesRead..]);
                _currentChunkOffset += bytesToCopy;
                bytesRead += bytesToCopy;
            }

            return bytesRead;
        }

        public override Int64 Seek(Int64 offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(Int64 value) => throw new NotSupportedException();

        public override void Write(Byte[] buffer, Int32 offset, Int32 count) => throw new NotSupportedException();

        private async Task LoadNextChunkAsync(CancellationToken cancellationToken)
        {
            var plaintextChunkLength = (Int32)Math.Min(_remainingPlaintextLength, _uploadedFile.ChunkSize);
            var encryptedChunkLength = checked(plaintextChunkLength + 16);
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
            _remainingPlaintextLength -= plaintextChunkLength;
            _nextChunkIndex++;

            if (_nextChunkIndex == _uploadedFile.ChunkCount)
            {
                var trailingByte = _encryptedContent.ReadByte();
                if (_remainingPlaintextLength != 0 || trailingByte != -1)
                {
                    throw new EndOfStreamException("Encrypted stream length did not match metadata.");
                }
            }
        }
    }

    private sealed record DirectHttpOpenResult(DownloadLookupStatus Status, Stream? Content);
}
