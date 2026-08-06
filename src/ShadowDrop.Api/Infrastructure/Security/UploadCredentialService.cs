// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Infrastructure.Security;

using ShadowDrop.Api.Shares;

/// <summary>
/// Creates and authenticates scoped upload credentials. Authentication rejects malformed tokens and unmatched
/// selectors without running the slow hash; once a selector matches a record the secret hash is always derived
/// and compared in fixed time before the lifecycle state is combined into the result, and no caller-visible
/// distinction is made between the failure causes. Credential validity is never cached across requests.
/// </summary>
public sealed class UploadCredentialService
{
    public const Int32 MaxNameLength = 100;
    private readonly ILogger<UploadCredentialService> _logger;
    private readonly IUploadCredentialRepository _repository;
    private readonly TimeProvider _timeProvider;

    public UploadCredentialService(
        IUploadCredentialRepository repository,
        TimeProvider timeProvider,
        ILogger<UploadCredentialService> logger)
    {
        _repository = repository;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<UploadCredentialAuthorizationContext?> AuthenticateAsync(String? bearerToken, CancellationToken cancellationToken)
    {
        if (!UploadCredentialToken.TryParse(bearerToken, out var selector, out var secret))
        {
            return null;
        }

        UploadCredentialRecord? credential;
        try
        {
            credential = await _repository.FindBySelectorDigestAsync(TokenHashing.ComputeHashBase64(selector), cancellationToken);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new UploadCredentialProviderUnavailableException(exception);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new UploadCredentialProviderUnavailableException(exception);
        }

        if (credential is null)
        {
            return null;
        }

        var now = _timeProvider.GetUtcNow();
        var secretMatches = UploadCredentialSecretHashing.Matches(secret, credential);
        var lifecycleValid = credential.RevokedAtUtc is null
                             && (credential.ExpiresAtUtc is null || credential.ExpiresAtUtc.Value > now);
        if (!(secretMatches & lifecycleValid))
        {
            return null;
        }

        try
        {
            await _repository.RecordUsageAsync(credential.CredentialId, now, cancellationToken);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new UploadCredentialProviderUnavailableException(exception);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new UploadCredentialProviderUnavailableException(exception);
        }

        return new(credential.CredentialId,
                   credential.MaxEncryptedFileBytes,
                   credential.MaxEncryptedShareBytes,
                   credential.ExpiresAtUtc);
    }

    public async Task<UploadCredentialCreationResult> CreateAsync(UploadCredentialCreationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var name = request.Name?.Trim();
        if (String.IsNullOrEmpty(name) || name.Length > MaxNameLength || name.Any(Char.IsControl))
        {
            throw new UploadCredentialValidationException(
                $"The credential name must contain 1 to {MaxNameLength} characters and no control characters.");
        }

        if (request.MaxEncryptedFileBytes is <= 0)
        {
            throw new UploadCredentialValidationException("The maximum encrypted file bytes must be positive.");
        }

        if (request.MaxEncryptedShareBytes is <= 0)
        {
            throw new UploadCredentialValidationException("The maximum aggregate encrypted share bytes must be positive.");
        }

        var now = NormalizeTimestamp(_timeProvider.GetUtcNow());
        var expiresAtUtc = request.ExpiresAtUtc is { } expiration ? NormalizeTimestamp(expiration) : (DateTimeOffset?)null;
        if (expiresAtUtc is { } expirationTimestamp && expirationTimestamp <= now)
        {
            throw new UploadCredentialValidationException("The expiration must lie in the future.");
        }

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var tokenParts = UploadCredentialToken.Create();
            var secretHash = UploadCredentialSecretHashing.Compute(tokenParts.Secret);
            var record = new UploadCredentialRecord(Guid.NewGuid(),
                                                    name,
                                                    now,
                                                    expiresAtUtc,
                                                    null,
                                                    null,
                                                    request.MaxEncryptedFileBytes,
                                                    request.MaxEncryptedShareBytes,
                                                    TokenHashing.ComputeHashBase64(tokenParts.Selector),
                                                    secretHash.HashBase64,
                                                    secretHash.SaltBase64,
                                                    secretHash.Iterations,
                                                    secretHash.Version);

            if (await _repository.TryCreateAsync(record, cancellationToken))
            {
                _logger.LogInformation("Upload credential created. CredentialId: {CredentialId}; Name: {Name}",
                                       record.CredentialId, record.Name);
                return new(record, tokenParts.Token);
            }
        }
    }

    private static DateTimeOffset NormalizeTimestamp(DateTimeOffset timestamp) =>
        DateTimeOffset.FromUnixTimeMilliseconds(timestamp.ToUnixTimeMilliseconds());
}
