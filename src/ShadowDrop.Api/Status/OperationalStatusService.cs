// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Status;

using ShadowDrop.Api.Configuration;
using ShadowDrop.Api.Health;
using ShadowDrop.Api.Infrastructure.Security;
using ShadowDrop.Api.Shares;
using ShadowDrop.Contracts;
using System.Reflection;

internal sealed class OperationalStatusService
{
    internal static readonly TimeSpan DefaultCollectionTimeout = TimeSpan.FromSeconds(5);

    private readonly StatusCapabilitiesContract _capabilities;
    private readonly CleanupRunStatus _cleanupRunStatus;
    private readonly String _metadataProvider;
    private readonly DateTimeOffset _processStartedAtUtc;
    private readonly IReadinessCheck _readinessCheck;
    private readonly IOperationalStatisticsProvider? _statisticsProvider;
    private readonly String _storageProvider;
    private readonly TimeProvider _timeProvider;

    public OperationalStatusService(
        IReadinessCheck readinessCheck,
        IEnumerable<IOperationalStatisticsProvider> statisticsProviders,
        CleanupRunStatus cleanupRunStatus,
        ShadowDropOptions options,
        TimeProvider timeProvider)
    {
        _readinessCheck = readinessCheck;
        _statisticsProvider = statisticsProviders.SingleOrDefault();
        _cleanupRunStatus = cleanupRunStatus;
        _timeProvider = timeProvider;
        _processStartedAtUtc = timeProvider.GetUtcNow();
        _capabilities = new(options.ApiExposure.EnablePublicDownloads,
                            options.ApiExposure.EnableAdminOperations,
                            options.ApiExposure.EnablePublicDownloads,
                            options.ApiExposure.UploadsEnabled);
        _metadataProvider = options.Metadata.Provider == MetadataProvider.LiteDb ? "litedb" : "mongodb";
        _storageProvider = options.Storage.Provider switch
        {
            BlobStorageProvider.FileSystem => "filesystem",
            BlobStorageProvider.MongoGridFs => "mongodb-gridfs",
            BlobStorageProvider.S3 => "s3",
            _ => "unsupported"
        };
    }

    internal TimeSpan CollectionTimeout { get; init; } = DefaultCollectionTimeout;

    public async Task<AdminServerStatusContract> GetAdminAsync(CancellationToken cancellationToken)
    {
        var collection = await CollectAdminAsync(cancellationToken);
        var now = _timeProvider.GetUtcNow();
        var storage = collection.Statistics?.Storage;
        var warnings = storage is { IsExact: false }
            ? new[] { OperationalStatusWarnings.StorageAccountingIncomplete }
            : [];
        var cleanup = _cleanupRunStatus.Snapshot;

        return new(OperationalStatusProtocol.CurrentVersion,
                   true,
                   collection.Readiness.Ready,
                   collection.Readiness.Reason,
                   _capabilities,
                   ResolveBuildVersion(),
                   Math.Max(0, (Int64)(now - _processStartedAtUtc).TotalSeconds),
                   collection.Readiness.Components
                             .Select(component => new StatusComponentContract(component.Name, component.State, component.Reason))
                             .ToArray(),
                   new(_metadataProvider, _storageProvider),
                   new(storage?.CompletedFileCount, storage?.TotalEncryptedBytes),
                   collection.Statistics is null ? null : Map(collection.Statistics.Shares),
                   new(cleanup.LastRunAtUtc, cleanup.LastOutcome),
                   new(null),
                   warnings);
    }

    public async Task<PublicServerStatusContract> GetPublicAsync(CancellationToken cancellationToken)
    {
        var readiness = await CollectReadinessAsync(cancellationToken);
        return new(OperationalStatusProtocol.CurrentVersion, true, readiness.Ready, readiness.Reason, _capabilities);
    }

    public async Task<UploadServerStatusContract> GetUploadAsync(
        UploadCredentialAuthorizationContext authorizationContext,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authorizationContext);
        var readiness = await CollectReadinessAsync(cancellationToken);
        return new(OperationalStatusProtocol.CurrentVersion,
                   true,
                   readiness.Ready,
                   readiness.Reason,
                   _capabilities,
                   new(authorizationContext.MaxEncryptedFileBytes,
                       authorizationContext.MaxEncryptedShareBytes,
                       authorizationContext.ExpiresAtUtc));
    }

    private static StatusSharesContract Map(ShareStatusCounts shares) =>
        new(shares.Active, shares.Expired, shares.Revoked, shares.CleanupPending, shares.CleanupFailed, shares.CleanupCompleted);

    private static String ResolveBuildVersion()
    {
        var informationalVersion = typeof(OperationalStatusService).Assembly
                                                                   .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                                                                   .InformationalVersion;
        var version = informationalVersion ?? typeof(OperationalStatusService).Assembly.GetName().Version?.ToString() ?? "unknown";
        var buildMetadataIndex = version.IndexOf('+', StringComparison.Ordinal);
        return buildMetadataIndex < 0 ? version : version[..buildMetadataIndex];
    }

    private async Task<OperationalStatusCollection> CollectAdminAsync(CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(CollectionTimeout);

        var now = _timeProvider.GetUtcNow();
        var readinessTask = _readinessCheck.GetStatusAsync(deadline.Token);
        Task<OperationalStatisticsSnapshot>? statisticsTask = null;
        String? synchronousFailureReason = null;
        try
        {
            statisticsTask = _statisticsProvider?.GetAsync(now, deadline.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            synchronousFailureReason = OperationalStatusReasons.DependencyTimeout;
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            synchronousFailureReason = OperationalStatusReasons.DependencyUnavailable;
        }

        var readiness = await readinessTask;
        if (synchronousFailureReason is not null)
        {
            return new(readiness.WithComponentFailure("metadata", synchronousFailureReason), null);
        }

        if (statisticsTask is null)
        {
            return new(readiness, null);
        }

        try
        {
            return new(readiness, await statisticsTask);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(readiness.WithComponentFailure("metadata", OperationalStatusReasons.DependencyTimeout), null);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return new(readiness.WithComponentFailure("metadata", OperationalStatusReasons.DependencyUnavailable), null);
        }
    }

    private async Task<OperationalReadinessSnapshot> CollectReadinessAsync(CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(CollectionTimeout);
        try
        {
            return await _readinessCheck.GetStatusAsync(deadline.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(false, OperationalStatusReasons.DependencyTimeout, []);
        }
    }

    private sealed record OperationalStatusCollection(
        OperationalReadinessSnapshot Readiness,
        OperationalStatisticsSnapshot? Statistics);
}
