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
            var decryptedContent = await TryDecryptDirectHttpContentAsync(share.ShareId,
                                                                          uploadedFile,
                                                                          presentedKeyMaterial!,
                                                                          cancellationToken);
            if (decryptedContent is null)
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
                           decryptedContent));
        }

        var encryptedContent = await _blobStorage.OpenReadAsync(uploadedFile.BlobKey, cancellationToken);
        return new(DownloadLookupStatus.Success,
                   new(mode,
                       share.ShareId,
                       fileId,
                       fileEntry.DisplayName ?? fileEntry.OriginalFileName,
                       uploadedFile.ContentType ?? "application/octet-stream",
                       uploadedFile.EncryptedLength,
                       encryptedContent));
    }

    private async Task<MemoryStream?> TryDecryptDirectHttpContentAsync(Guid shareId,
                                                                       UploadedFileRecord uploadedFile,
                                                                       String keyMaterial,
                                                                       CancellationToken cancellationToken)
    {
        try
        {
            var secretBytes = Convert.FromBase64String(keyMaterial);
            var kdfSalt = Convert.FromBase64String(uploadedFile.KdfSaltBase64);
            await using var encryptedContent = await _blobStorage.OpenReadAsync(uploadedFile.BlobKey, cancellationToken);
            using var shareSecret = ShareSecret.FromBytes(secretBytes);
            var context = new FileEncryptionContext(shareId, uploadedFile.FileId, kdfSalt);
            using var contentKey = ChunkEncryptionService.DeriveContentKey(shareSecret, context);
            var plaintext = new MemoryStream(uploadedFile.PlaintextLength <= Int32.MaxValue ? (Int32)uploadedFile.PlaintextLength : 0);
            var remainingPlaintextLength = uploadedFile.PlaintextLength;

            for (var chunkIndex = 0L; chunkIndex < uploadedFile.ChunkCount; chunkIndex++)
            {
                var plaintextChunkLength = (Int32)Math.Min(remainingPlaintextLength, uploadedFile.ChunkSize);
                var encryptedChunkLength = checked(plaintextChunkLength + 16);
                var encryptedChunkBytes = new Byte[encryptedChunkLength];
                await encryptedContent.ReadExactlyAsync(encryptedChunkBytes, cancellationToken);

                var metadata = new ChunkMetadata(CryptoVersion.V1,
                                                 CryptoAlgorithm.Aes256Gcm,
                                                 shareId,
                                                 uploadedFile.FileId,
                                                 uploadedFile.ChunkSize,
                                                 chunkIndex,
                                                 plaintextChunkLength);
                var decryptedChunk = ChunkEncryptionService.DecryptChunk(new(encryptedChunkBytes), contentKey, metadata);
                await plaintext.WriteAsync(decryptedChunk, cancellationToken);
                remainingPlaintextLength -= plaintextChunkLength;
            }

            if (remainingPlaintextLength != 0 || encryptedContent.ReadByte() != -1)
            {
                return null;
            }

            plaintext.Position = 0;
            return plaintext;
        }
        catch (Exception exception) when (exception is ArgumentException
                                                       or CryptographicException
                                                       or EndOfStreamException
                                                       or FormatException
                                                       or OverflowException)
        {
            return null;
        }
    }
}
