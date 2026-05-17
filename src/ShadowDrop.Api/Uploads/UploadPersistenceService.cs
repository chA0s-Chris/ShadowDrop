// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Uploads;

public sealed class UploadPersistenceService
{
    private readonly IBlobStorage _blobStorage;
    private readonly IUploadedFileMetadataRepository _metadataRepository;

    public UploadPersistenceService(IBlobStorage blobStorage, IUploadedFileMetadataRepository metadataRepository)
    {
        _blobStorage = blobStorage;
        _metadataRepository = metadataRepository;
    }

    public async Task<UploadResult> PersistAsync(UploadPersistenceRequest request, Stream encryptedContent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(encryptedContent);

        UploadBlobDescriptor? blob = null;

        try
        {
            blob = await _blobStorage.SaveAsync(request.FileId, encryptedContent, cancellationToken);
            if (blob.WrittenLength != request.EncryptedLength)
            {
                throw new UploadValidationException("The encrypted content length does not match the declared metadata.");
            }

            var record = new UploadedFileRecord(request.FileId,
                                                blob.BlobKey,
                                                request.OriginalFileName,
                                                request.PlaintextLength,
                                                request.EncryptedLength,
                                                request.ContentType,
                                                request.EncryptionFormatVersion,
                                                request.AlgorithmId,
                                                request.ChunkSize,
                                                request.ChunkCount,
                                                request.KdfSaltBase64,
                                                request.PlaintextSha256);

            if (!await _metadataRepository.TryCompleteReservationAsync(record, cancellationToken))
            {
                throw new UploadValidationException("The file id is invalid or no longer available.");
            }

            return new(record.FileId,
                       record.PlaintextLength,
                       record.EncryptedLength,
                       record.ChunkSize,
                       record.ChunkCount,
                       record.EncryptionFormatVersion,
                       record.AlgorithmId);
        }
        catch
        {
            if (blob is not null)
            {
                try
                {
                    await _blobStorage.DeleteIfExistsAsync(blob.BlobKey, CancellationToken.None);
                }
                catch
                {
                    // preserve the original upload failure while attempting deterministic cleanup
                }
            }

            throw;
        }
    }
}
