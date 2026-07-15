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
    private readonly ILogger<CreateShareService> _logger;
    private readonly IShareMetadataRepository _shareMetadataRepository;
    private readonly TimeProvider _timeProvider;
    private readonly IUploadedFileMetadataRepository _uploadedFileMetadataRepository;

    public CreateShareService(IUploadedFileMetadataRepository uploadedFileMetadataRepository,
                              IShareMetadataRepository shareMetadataRepository,
                              TimeProvider timeProvider,
                              ILogger<CreateShareService> logger)
    {
        _uploadedFileMetadataRepository = uploadedFileMetadataRepository;
        _shareMetadataRepository = shareMetadataRepository;
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
            ValidateRequest(request);

            var distinctFileIds = new HashSet<Guid>();
            var files = new List<ShareFileEntryRecord>(request.Files!.Count);
            var aggregateEncryptedBytes = 0L;
            foreach (var fileRequest in request.Files)
            {
                if (!distinctFileIds.Add(fileRequest.FileId))
                {
                    throw new CreateShareValidationException("Duplicate file ids are not allowed.");
                }

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

            var shareId = Guid.NewGuid();
            var createdAtUtc = _timeProvider.GetUtcNow();
            var shareToken = GenerateOpaqueToken();
            var downloadBearerToken = request.GenerateDownloadBearerToken == true ? GenerateOpaqueToken() : null;
            var record = new ShareRecord(shareId,
                                         TokenHashing.ComputeHashBase64(shareToken),
                                         createdAtUtc,
                                         request.ExpiresAtUtc.ToUniversalTime(),
                                         null,
                                         ShareCleanupState.Pending,
                                         request.DirectHttpEnabled ?? false,
                                         downloadBearerToken is null
                                             ? null
                                             : new DownloadBearerTokenRecord(TokenHashing.ComputeHashBase64(downloadBearerToken),
                                                                             request.DownloadBearerTokenExpiresAtUtc!.Value.ToUniversalTime()),
                                         files,
                                         authorizationContext.CredentialId);

            await _shareMetadataRepository.CreateAsync(record, cancellationToken);

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

    private static void ValidateRequest(CreateShareRequest request)
    {
        if (request.Files is null || request.Files.Count == 0)
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

        if (request.GenerateDownloadBearerToken == true)
        {
            if (request.DownloadBearerTokenExpiresAtUtc is null || request.DownloadBearerTokenExpiresAtUtc == default)
            {
                throw new CreateShareValidationException("An expiration timestamp is required when generating a download bearer token.");
            }
        }
        else if (request.DownloadBearerTokenExpiresAtUtc is not null)
        {
            throw new CreateShareValidationException("Download bearer token expiration requires token generation.");
        }
    }
}
