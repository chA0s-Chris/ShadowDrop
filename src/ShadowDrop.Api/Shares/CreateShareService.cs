// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Shares;

using ShadowDrop.Api.Uploads;
using System.Security.Cryptography;
using System.Text;

public sealed class CreateShareService
{
    private const Int32 MinimumTokenEntropyBytes = 32;
    private readonly IShareMetadataRepository _shareMetadataRepository;
    private readonly TimeProvider _timeProvider;
    private readonly IUploadedFileMetadataRepository _uploadedFileMetadataRepository;

    public CreateShareService(IUploadedFileMetadataRepository uploadedFileMetadataRepository,
                              IShareMetadataRepository shareMetadataRepository,
                              TimeProvider timeProvider)
    {
        _uploadedFileMetadataRepository = uploadedFileMetadataRepository;
        _shareMetadataRepository = shareMetadataRepository;
        _timeProvider = timeProvider;
    }

    public async Task<CreateShareResult> CreateAsync(CreateShareRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        ValidateRequest(request);

        var distinctFileIds = new HashSet<Guid>();
        var files = new List<ShareFileEntryRecord>(request.Files!.Count);
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

            files.Add(new ShareFileEntryRecord(fileRequest.FileId, uploadedFile.OriginalFileName, NormalizeDisplayName(fileRequest.DisplayName)));
        }

        var shareId = Guid.NewGuid();
        var createdAtUtc = _timeProvider.GetUtcNow();
        var shareToken = GenerateOpaqueToken();
        var downloadBearerToken = request.GenerateDownloadBearerToken == true ? GenerateOpaqueToken() : null;
        var record = new ShareRecord(shareId,
                                     ComputeHashBase64(shareToken),
                                     createdAtUtc,
                                     request.ExpiresAtUtc.ToUniversalTime(),
                                     null,
                                     ShareCleanupState.Pending,
                                     request.DirectHttpEnabled ?? false,
                                     downloadBearerToken is null
                                         ? null
                                         : new DownloadBearerTokenRecord(ComputeHashBase64(downloadBearerToken),
                                                                         request.DownloadBearerTokenExpiresAtUtc!.Value.ToUniversalTime()),
                                     files);

        await _shareMetadataRepository.CreateAsync(record, cancellationToken);

        return new CreateShareResult(shareId, shareToken, downloadBearerToken);
    }

    private static String ComputeHashBase64(String token)
    {
        var tokenBytes = Encoding.UTF8.GetBytes(token);
        var hashBytes = SHA256.HashData(tokenBytes);
        return Convert.ToBase64String(hashBytes);
    }

    private static String GenerateOpaqueToken()
    {
        var tokenBytes = RandomNumberGenerator.GetBytes(MinimumTokenEntropyBytes);
        return Convert.ToBase64String(tokenBytes)
                      .Replace('+', '-')
                      .Replace('/', '_')
                      .TrimEnd('=');
    }

    private static String? NormalizeDisplayName(String? displayName) =>
        String.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim();

    private static void ValidateRequest(CreateShareRequest request)
    {
        if ((request.Files is null) || (request.Files.Count == 0))
        {
            throw new CreateShareValidationException("At least one file is required.");
        }

        if (request.ExpiresAtUtc == default)
        {
            throw new CreateShareValidationException("A share expiration timestamp is required.");
        }

        var directHttpEnabled = request.DirectHttpEnabled ?? false;
        if (directHttpEnabled && (request.GenerateDownloadBearerToken == true))
        {
            throw new CreateShareValidationException("Direct HTTP shares cannot require a download bearer token.");
        }

        if (!directHttpEnabled && (request.GenerateDownloadBearerToken is null))
        {
            throw new CreateShareValidationException("Separate-key mode requires explicit bearer-token configuration.");
        }

        if (request.GenerateDownloadBearerToken == true)
        {
            if ((request.DownloadBearerTokenExpiresAtUtc is null) || (request.DownloadBearerTokenExpiresAtUtc == default))
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
