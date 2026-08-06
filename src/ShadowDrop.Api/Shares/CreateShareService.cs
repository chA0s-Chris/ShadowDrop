// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Shares;

using ShadowDrop.Api.Infrastructure.Security;
using ShadowDrop.Api.Uploads;
using ShadowDrop.Contracts;
using System.Security.Cryptography;

public sealed class CreateShareService
{
    private const Int32 MinimumTokenEntropyBytes = 32;
    private readonly ShareCreationClaimReconciler _claimReconciler;
    private readonly ILogger<CreateShareService> _logger;
    private readonly IShareOperationClaimRepository _operationClaimRepository;
    private readonly IShareMetadataRepository _shareMetadataRepository;
    private readonly TimeProvider _timeProvider;
    private readonly IUploadedFileMetadataRepository _uploadedFileMetadataRepository;

    public CreateShareService(IUploadedFileMetadataRepository uploadedFileMetadataRepository,
                              IShareMetadataRepository shareMetadataRepository,
                              IShareOperationClaimRepository operationClaimRepository,
                              ShareCreationClaimReconciler claimReconciler,
                              TimeProvider timeProvider,
                              ILogger<CreateShareService> logger)
    {
        _uploadedFileMetadataRepository = uploadedFileMetadataRepository;
        _shareMetadataRepository = shareMetadataRepository;
        _operationClaimRepository = operationClaimRepository;
        _claimReconciler = claimReconciler;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<CreateShareResult> CreateAsync(CreateShareRequest request, CancellationToken cancellationToken)
        => await CreateAsync(request,
                             UploadCredentialAuthorizationContext.BootstrapAdmin,
                             cancellationToken);

    public async Task<CreateShareResult> CreateAsync(CreateShareRequest request,
                                                     UploadCredentialAuthorizationContext authorizationContext,
                                                     CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(authorizationContext);

        try
        {
            var validatedRequest = ValidateRequest(request);

            var fileIds = validatedRequest.Files.Select(file => file.FileId).ToArray();
            if (fileIds.Distinct().Count() != fileIds.Length)
            {
                throw new CreateShareValidationException("Duplicate file ids are not allowed.");
            }

            await _claimReconciler.ReconcileAsync(fileIds, cancellationToken);

            var shareId = Guid.NewGuid();
            var operationId = Guid.NewGuid();
            var shareToken = GenerateOpaqueToken();
            var downloadBearerToken = request.GenerateDownloadBearerToken == true ? GenerateOpaqueToken() : null;
            var claim = await _operationClaimRepository.TryAcquireAsync(operationId,
                                                                        ShareOperationClaimKind.CreateShare,
                                                                        shareId,
                                                                        fileIds,
                                                                        cancellationToken);
            if (claim is null)
            {
                throw new CreateShareValidationException("All referenced files must be unused by another share operation.");
            }

            var commitStarted = false;
            try
            {
                var files = new List<ShareFileEntryRecord>(validatedRequest.Files.Count);
                var aggregateEncryptedBytes = 0L;
                foreach (var fileRequest in validatedRequest.Files)
                {
                    var uploadedFile = await _uploadedFileMetadataRepository.GetAsync(fileRequest.FileId, cancellationToken);
                    if (uploadedFile is null)
                    {
                        throw new CreateShareValidationException("All referenced files must exist.");
                    }

                    if (!authorizationContext.IsBootstrapAdmin
                        && uploadedFile.OwnerCredentialId != authorizationContext.CredentialId)
                    {
                        throw new CreateShareValidationException("All referenced files must exist.");
                    }

                    try
                    {
                        aggregateEncryptedBytes = checked(aggregateEncryptedBytes + uploadedFile.EncryptedLength);
                    }
                    catch (OverflowException exception)
                    {
                        throw new CreateShareValidationException("The aggregate encrypted share size is invalid.", exception);
                    }

                    files.Add(new(fileRequest.FileId, uploadedFile.OriginalFileName, DisplayNameNormalizer.Normalize(fileRequest.DisplayName)));
                }

                if (authorizationContext.MaxEncryptedShareBytes is { } maxEncryptedShareBytes
                    && aggregateEncryptedBytes > maxEncryptedShareBytes)
                {
                    throw new CreateShareValidationException("The aggregate encrypted share size exceeds the credential limit.");
                }

                var createdAtUtc = _timeProvider.GetUtcNow();
                DownloadBearerTokenRecord? downloadBearerTokenRecord = null;
                if (downloadBearerToken is not null)
                {
                    // Both the token and the expiration are driven by GenerateDownloadBearerToken, so ValidateRequest
                    // has proven the expiration is present here. Storing the share without the record while the caller
                    // receives the token would leave the share downloadable without it, so a broken invariant must fail.
                    var bearerTokenExpiresAtUtc = validatedRequest.DownloadBearerTokenExpiresAtUtc
                                                  ?? throw new InvalidOperationException(
                                                      "A generated download bearer token requires an expiration timestamp.");

                    downloadBearerTokenRecord = new(TokenHashing.ComputeHashBase64(downloadBearerToken),
                                                    bearerTokenExpiresAtUtc.ToUniversalTime());
                }

                var record = new ShareRecord(shareId,
                                             TokenHashing.ComputeHashBase64(shareToken),
                                             createdAtUtc,
                                             request.ExpiresAtUtc.ToUniversalTime(),
                                             null,
                                             ShareCleanupState.Pending,
                                             request.DirectHttpEnabled ?? false,
                                             downloadBearerTokenRecord,
                                             files,
                                             authorizationContext.CredentialId);

                if (!await _operationClaimRepository.TryBeginCommitAsync(operationId, record, cancellationToken))
                {
                    throw new CreateShareValidationException("Share creation was superseded before it could commit. Retry the request.");
                }

                commitStarted = true;
                await _shareMetadataRepository.CreateAsync(record, cancellationToken);
                await _operationClaimRepository.TryReleaseAsync(operationId, cancellationToken);

                _logger.LogInformation(
                    "Share created. ShareId: {ShareId}; FileCount: {FileCount}; ExpiresAtUtc: {ExpiresAtUtc}; DirectHttpEnabled: {DirectHttpEnabled}; " +
                    "HasDownloadBearerToken: {HasDownloadBearerToken}",
                    shareId,
                    files.Count,
                    record.ExpiresAtUtc,
                    record.DirectHttpEnabled,
                    downloadBearerToken is not null);

                return new(shareId, shareToken, downloadBearerToken);
            }
            catch (Exception exception)
            {
                if (!commitStarted)
                {
                    await TryCleanupClaimAsync(operationId, false);
                }
                else if (exception is CreateShareValidationException)
                {
                    await TryCleanupClaimAsync(operationId, true);
                }

                throw;
            }
        }
        catch (CreateShareValidationException exception)
        {
            _logger.LogWarning(exception, "Share creation rejected");
            throw;
        }
    }

    private static String GenerateOpaqueToken()
    {
        var tokenBytes = RandomNumberGenerator.GetBytes(MinimumTokenEntropyBytes);
        return Convert.ToBase64String(tokenBytes)
                      .Replace('+', '-')
                      .Replace('/', '_')
                      .TrimEnd('=');
    }

    /// <summary>
    /// Validates the request and returns the values it proves to be present, so that callers can consume them
    /// without repeating the null checks the compiler cannot carry across the method boundary.
    /// </summary>
    private static ValidatedCreateShareRequest ValidateRequest(CreateShareRequest request)
    {
        if (request.Files is not { Count: > 0 } files)
        {
            throw new CreateShareValidationException("At least one file is required.");
        }

        if (request.ExpiresAtUtc == default)
        {
            throw new CreateShareValidationException("A share expiration timestamp is required.");
        }

        var directHttpEnabled = request.DirectHttpEnabled ?? false;
        if (directHttpEnabled && request.GenerateDownloadBearerToken == true)
        {
            throw new CreateShareValidationException("Direct HTTP shares cannot require a download bearer token.");
        }

        if (!directHttpEnabled && request.GenerateDownloadBearerToken is null)
        {
            throw new CreateShareValidationException("Separate-key mode requires explicit bearer-token configuration.");
        }

        DateTimeOffset? downloadBearerTokenExpiresAtUtc = null;
        if (request.GenerateDownloadBearerToken == true)
        {
            if (request.DownloadBearerTokenExpiresAtUtc is not { } expiresAtUtc || expiresAtUtc == default)
            {
                throw new CreateShareValidationException("An expiration timestamp is required when generating a download bearer token.");
            }

            downloadBearerTokenExpiresAtUtc = expiresAtUtc;
        }
        else if (request.DownloadBearerTokenExpiresAtUtc is not null)
        {
            throw new CreateShareValidationException("Download bearer token expiration requires token generation.");
        }

        return new(files, downloadBearerTokenExpiresAtUtc);
    }

    /// <summary>
    /// Releases or aborts the operation claim without letting a claim-store failure mask the share-creation
    /// failure that is already on its way out. An orphaned claim is not lost work: a later request or cleanup
    /// run reconciles it via <see cref="ShareCreationClaimReconciler"/>.
    /// </summary>
    private async Task TryCleanupClaimAsync(Guid operationId, Boolean commitStarted)
    {
        try
        {
            if (commitStarted)
            {
                await _operationClaimRepository.TryReleaseAsync(operationId, CancellationToken.None);
            }
            else
            {
                await _operationClaimRepository.TryAbortAcquiredAsync(operationId, CancellationToken.None);
            }
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception,
                               "Share creation could not clean up its operation claim; a later request will reconcile it. OperationId: {OperationId}",
                               operationId);
        }
    }

    /// <summary>
    /// Carries the values <see cref="ValidateRequest"/> has proven to be present.
    /// <see cref="DownloadBearerTokenExpiresAtUtc"/> is set exactly when a download bearer token is generated.
    /// </summary>
    private sealed record ValidatedCreateShareRequest(
        IReadOnlyList<CreateShareFileRequest> Files,
        DateTimeOffset? DownloadBearerTokenExpiresAtUtc);
}
