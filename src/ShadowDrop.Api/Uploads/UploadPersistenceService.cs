// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Uploads;

using ShadowDrop.Api.Infrastructure.Security;

public sealed class UploadPersistenceService
{
    private readonly IBlobStorage _blobStorage;
    private readonly ILogger<UploadPersistenceService> _logger;
    private readonly IUploadedFileMetadataRepository _metadataRepository;

    public UploadPersistenceService(IBlobStorage blobStorage,
                                    IUploadedFileMetadataRepository metadataRepository,
                                    ILogger<UploadPersistenceService> logger)
    {
        _blobStorage = blobStorage;
        _metadataRepository = metadataRepository;
        _logger = logger;
    }

    public async Task<UploadResult> PersistAsync(UploadPersistenceRequest request, Stream encryptedContent, CancellationToken cancellationToken)
        => await PersistAsync(request,
                              encryptedContent,
                              UploadCredentialAuthorizationContext.BootstrapAdmin,
                              cancellationToken);

    public async Task<UploadResult> PersistAsync(UploadPersistenceRequest request,
                                                 Stream encryptedContent,
                                                 UploadCredentialAuthorizationContext authorizationContext,
                                                 CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(encryptedContent);
        ArgumentNullException.ThrowIfNull(authorizationContext);

        UploadBlobDescriptor? blob = null;
        var reservationClaimed = false;
        var reservationCompleted = false;

        try
        {
            reservationClaimed = authorizationContext.CredentialId is { } ownerCredentialId
                ? await _metadataRepository.TryClaimReservationAsync(request.FileId, ownerCredentialId, cancellationToken)
                : await _metadataRepository.TryClaimReservationAsync(request.FileId, cancellationToken);
            if (!reservationClaimed)
            {
                throw new UploadValidationException("The file id is invalid or no longer available.");
            }

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
                                                request.PlaintextSha256,
                                                authorizationContext.CredentialId);

            reservationCompleted = await _metadataRepository.TryCompleteReservationAsync(record, cancellationToken);
            if (!reservationCompleted)
            {
                throw new UploadValidationException("The file id is invalid or no longer available.");
            }

            _logger.LogInformation(
                "Upload completed. FileId: {FileId}; BlobKey: {BlobKey}; PlaintextLength: {PlaintextLength}; EncryptedLength: {EncryptedLength}; " +
                "ChunkSize: {ChunkSize}; ChunkCount: {ChunkCount}; ContentType: {ContentType}",
                record.FileId,
                record.BlobKey,
                record.PlaintextLength,
                record.EncryptedLength,
                record.ChunkSize,
                record.ChunkCount,
                record.ContentType);

            return new(record.FileId,
                       record.PlaintextLength,
                       record.EncryptedLength,
                       record.ChunkSize,
                       record.ChunkCount,
                       record.EncryptionFormatVersion,
                       record.AlgorithmId);
        }
        catch (Exception exception)
        {
            switch (exception)
            {
                case UploadValidationException:
                    _logger.LogWarning(exception, "Upload rejected. FileId: {FileId}", request.FileId);
                    break;
                case UploadPayloadTooLargeException:
                    _logger.LogWarning(exception, "Upload rejected because the payload exceeded the configured limit. FileId: {FileId}", request.FileId);
                    break;
                case OperationCanceledException:
                    _logger.LogInformation(exception,
                                           "Upload canceled. FileId: {FileId}; CancellationRequested: {CancellationRequested}",
                                           request.FileId,
                                           cancellationToken.IsCancellationRequested);
                    break;
                default:
                    _logger.LogError(exception, "Upload failed unexpectedly. FileId: {FileId}", request.FileId);
                    break;
            }

            if (blob is not null)
            {
                try
                {
                    _ = await _blobStorage.DeleteIfExistsAsync(blob.BlobKey, CancellationToken.None);
                }
                catch
                {
                    // preserve the original upload failure while attempting deterministic cleanup
                }
            }

            if (reservationClaimed && !reservationCompleted)
            {
                try
                {
                    await _metadataRepository.ReleaseClaimAsync(request.FileId, CancellationToken.None);
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
