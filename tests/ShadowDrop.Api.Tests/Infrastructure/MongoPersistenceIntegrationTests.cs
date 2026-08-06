// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Infrastructure;

using Chaos.Mongo;
using Chaos.Mongo.Configuration;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using NUnit.Framework;
using ShadowDrop.Api;
using ShadowDrop.Api.Configuration;
using ShadowDrop.Api.Infrastructure.Mongo;
using ShadowDrop.Api.Infrastructure.Security;
using ShadowDrop.Api.Shares;
using ShadowDrop.Api.Uploads;
using ShadowDrop.Contracts;
using ShadowDrop.Tests.Infrastructure.Security;
using ShadowDrop.Tests.Uploads;
using System.Net;
using System.Text.Json;
using Testcontainers.MongoDb;

[Category("MongoIntegration")]
[NonParallelizable]
public abstract class MongoPersistenceIntegrationTests
{
    private MongoDbContainer _container;
    private IMongoHelper _mongo;
    private ServiceProvider _services;

    protected abstract String MongoImage { get; }

    protected virtual Boolean UseReplicaSet => false;

    [Test]
    public async Task AdminCredentialRepository_ShouldAllowOnlyOneConcurrentBootstrapWinner()
    {
        var repository = _services.GetRequiredService<MongoAdminTokenCredentialRepository>();
        var attempts = await Task.WhenAll(
            repository.TryCreateAsync(new("hash-a", "salt-a", 1), CancellationToken.None),
            repository.TryCreateAsync(new("hash-b", "salt-b", 2), CancellationToken.None));
        attempts.Count(x => x).Should().Be(1);
        (await repository.GetAsync(CancellationToken.None)).Should().NotBeNull();
    }

    [Test]
    public async Task AllFourProviderCombinations_ShouldCompleteApplicationPersistenceWorkflow()
    {
        var loggerFactory = _services.GetRequiredService<ILoggerFactory>();
        foreach (var metadataProvider in Enum.GetValues<MetadataProvider>())
        {
            // S3 needs a live object-store container; RustFsS3IntegrationTests covers that combination.
            foreach (var blobProvider in Enum.GetValues<BlobStorageProvider>().Except([BlobStorageProvider.S3]))
            {
                var root = Path.Combine(Path.GetTempPath(), $"shadowdrop-matrix-{Guid.NewGuid():N}");
                Directory.CreateDirectory(root);
                var options = new ShadowDropOptions
                {
                    Metadata = new()
                    {
                        Provider = metadataProvider,
                        LiteDbPath = Path.Combine(root, "metadata", "shadowdrop.db")
                    },
                    Storage = new()
                    {
                        Provider = blobProvider,
                        LocalRoot = Path.Combine(root, "blobs"),
                        GridFsBucketName = "shadowdrop_test_blobs"
                    }
                };
                var disposables = new List<IDisposable>();
                try
                {
                    IUploadedFileMetadataRepository uploads;
                    IShareMetadataRepository shares;
                    if (metadataProvider == MetadataProvider.LiteDb)
                    {
                        var uploadedRepository = new LiteDbUploadedFileMetadataRepository(
                            options, loggerFactory.CreateLogger<LiteDbUploadedFileMetadataRepository>());
                        var shareRepository = new LiteDbShareMetadataRepository(options);
                        disposables.Add(uploadedRepository);
                        disposables.Add(shareRepository);
                        uploads = uploadedRepository;
                        shares = shareRepository;
                    }
                    else
                    {
                        uploads = _services.GetRequiredService<MongoUploadedFileMetadataRepository>();
                        shares = _services.GetRequiredService<MongoShareMetadataRepository>();
                    }

                    IBlobStorage blobs = blobProvider == BlobStorageProvider.FileSystem
                        ? new LocalBlobStorage(options, loggerFactory.CreateLogger<LocalBlobStorage>())
                        : _services.GetRequiredService<MongoGridFsBlobStorage>();

                    var fileId = await uploads.ReserveFileIdAsync(CancellationToken.None);
                    (await uploads.TryClaimReservationAsync(fileId, CancellationToken.None)).Should().BeTrue();
                    var descriptor = await blobs.SaveAsync(fileId, new MemoryStream([1, 2, 3, 4]), CancellationToken.None);
                    (await uploads.TryCompleteReservationAsync(CreateUploadedFile(fileId, descriptor.BlobKey, 4), CancellationToken.None))
                        .Should().BeTrue();
                    await shares.CreateAsync(CreateShare(Guid.NewGuid(), $"matrix-{Guid.NewGuid():N}", fileId), CancellationToken.None);
                    (await uploads.GetAsync(fileId, CancellationToken.None)).Should().NotBeNull();
                    _ = await blobs.DeleteIfExistsAsync(descriptor.BlobKey, CancellationToken.None);
                }
                finally
                {
                    disposables.ForEach(x => x.Dispose());
                    Directory.Delete(root, true);
                }
            }
        }
    }

    [Test]
    public async Task AllFourProviderCombinations_ShouldStartApplicationAndServeRequests()
    {
        foreach (var metadataProvider in Enum.GetValues<MetadataProvider>())
        {
            // S3 needs a live object-store container; RustFsS3IntegrationTests covers that combination.
            foreach (var blobProvider in Enum.GetValues<BlobStorageProvider>().Except([BlobStorageProvider.S3]))
            {
                await using var factory = new ProviderMatrixApiFactory(
                    metadataProvider, blobProvider,
                    _container.GetConnectionString(), _mongo.Database.DatabaseNamespace.DatabaseName);
                using var client = factory.CreateClient();

                using var response = await client.GetAsync("/health/ready");
                using var statusResponse = await client.GetAsync("/api/status");

                response.StatusCode.Should().Be(
                    HttpStatusCode.OK,
                    $"the application must start and serve requests with {metadataProvider} metadata and {blobProvider} blobs");
                statusResponse.StatusCode.Should().Be(
                    HttpStatusCode.OK,
                    $"the status projection must be ready with {metadataProvider} metadata and {blobProvider} blobs");
                using var status = JsonDocument.Parse(await statusResponse.Content.ReadAsStringAsync());
                status.RootElement.GetProperty("ready").GetBoolean().Should().BeTrue();
                status.RootElement.GetProperty("reason").GetString().Should().Be(OperationalStatusReasons.None);
            }
        }
    }

    [Test]
    public async Task BlobStorageContract_ShouldPass_ForBothImplementations()
    {
        var root = Path.Combine(Path.GetTempPath(), $"shadowdrop-blob-contract-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var options = new ShadowDropOptions
            {
                Storage = new()
                {
                    LocalRoot = Path.Combine(root, "blobs")
                }
            };
            var loggerFactory = _services.GetRequiredService<ILoggerFactory>();
            await BlobStorageContract.AssertAsync(
                new LocalBlobStorage(options, loggerFactory.CreateLogger<LocalBlobStorage>()));
            await BlobStorageContract.AssertAsync(_services.GetRequiredService<MongoGridFsBlobStorage>());
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Test]
    public async Task ChaosMongoLock_ShouldCoordinateAcrossConcurrentCallers()
    {
        await using var first = await _mongo.TryAcquireLockAsync("integration-cleanup", TimeSpan.FromMinutes(1));
        first.Should().NotBeNull();
        var second = await _mongo.TryAcquireLockAsync("integration-cleanup", TimeSpan.FromMinutes(1));
        second.Should().BeNull();
    }

    [Test]
    public async Task Configurator_ShouldCreateRequiredIndexesIdempotently()
    {
        await _services.GetRequiredService<IMongoConfiguratorRunner>().RunConfiguratorsAsync();
        var uploadIndexes = await (await _mongo.GetCollection<MongoUploadedFileDocument>().Indexes.ListAsync())
            .ToListAsync();
        var shareIndexes = await (await _mongo.GetCollection<MongoShareDocument>().Indexes.ListAsync())
            .ToListAsync();
        var uploadCredentialIndexes = await (await _mongo.GetCollection<MongoUploadCredentialDocument>().Indexes.ListAsync())
            .ToListAsync();
        var claimIndexes = await (await _mongo.GetCollection<MongoShareOperationClaimDocument>().Indexes.ListAsync())
            .ToListAsync();
        uploadIndexes.Select(x => x["name"].AsString).Should()
                     .Contain(["reservation_state", "storage_stats", "retention_stats", "unreferenced_upload_sweep"]);
        claimIndexes.Select(x => x["name"].AsString).Should().Contain(["claimed_file_unique", "claim_kind", "sweep_claim_recovery"]);
        shareIndexes.Select(x => x["name"].AsString).Should().Contain(
        [
            "share_token_unique", "file_single_use", "cleanup_candidates", "newest_first_listing",
            "share_expiration", "share_cleanup_state", "share_lifecycle"
        ]);
        uploadCredentialIndexes.Select(x => x["name"].AsString).Should().Contain(
            ["selector_digest_unique", "newest_first_listing"]);
    }

    [Test]
    public async Task GridFsStorage_ShouldStreamSeekDeleteAndCleanUpFailedUploads()
    {
        var storage = _services.GetRequiredService<MongoGridFsBlobStorage>();
        var fileId = Guid.NewGuid();
        var content = Enumerable.Range(0, 700_000).Select(value => (Byte)(value % 251)).ToArray();
        var descriptor = await storage.SaveAsync(fileId, new MemoryStream(content), CancellationToken.None);
        descriptor.BlobKey.Should().Be(fileId.ToString("N"));
        descriptor.WrittenLength.Should().Be(content.Length);

        await using (var stream = await storage.OpenReadAsync(descriptor.BlobKey, CancellationToken.None))
        {
            stream.CanSeek.Should().BeTrue();
            stream.Seek(123_456, SeekOrigin.Begin);
            var buffer = new Byte[32];
            _ = await stream.ReadAsync(buffer);
            buffer.Should().Equal(content.Skip(123_456).Take(32));
        }

        (await storage.DeleteIfExistsAsync(descriptor.BlobKey, CancellationToken.None)).Should().BeTrue();
        (await storage.DeleteIfExistsAsync(descriptor.BlobKey, CancellationToken.None)).Should().BeFalse();
        var openMissing = async () => await storage.OpenReadAsync(descriptor.BlobKey, CancellationToken.None);
        await openMissing.Should().ThrowAsync<FileNotFoundException>();

        var failedId = Guid.NewGuid();
        var save = async () => await storage.SaveAsync(
            failedId, new FailAfterStream(content, 400_000, new IOException("injected upload failure")), CancellationToken.None);
        await save.Should().ThrowAsync<IOException>().WithMessage("injected upload failure");
        await AssertGridFsUploadWasRemovedAsync(failedId);

        var cancelledId = Guid.NewGuid();
        var cancelledSave = async () => await storage.SaveAsync(
            cancelledId, new FailAfterStream(content, 400_000, new OperationCanceledException("injected cancellation")), CancellationToken.None);
        await cancelledSave.Should().ThrowAsync<OperationCanceledException>().WithMessage("injected cancellation");
        await AssertGridFsUploadWasRemovedAsync(cancelledId);
    }

    [Test]
    public async Task ShareCleanupRunner_ShouldSkip_WhenAnotherInstanceOwnsDistributedLock()
    {
        var service = new ShareCleanupService(
            _services.GetRequiredService<MongoShareMetadataRepository>(),
            _services.GetRequiredService<MongoUploadedFileMetadataRepository>(),
            _services.GetRequiredService<MongoShareOperationClaimRepository>(),
            _services.GetRequiredService<MongoGridFsBlobStorage>(),
            TimeProvider.System,
            _services.GetRequiredService<ILoggerFactory>().CreateLogger<ShareCleanupService>());
        using var coordinator = new MongoShareCleanupCoordinator(_mongo);
        var runner = new ShareCleanupRunner(
            service,
            CreateSweepService(),
            coordinator,
            _services.GetRequiredService<ILoggerFactory>().CreateLogger<ShareCleanupRunner>());
        await using var heldLock = await _mongo.TryAcquireLockAsync("shadowdrop-share-cleanup", TimeSpan.FromMinutes(1));

        var result = await runner.RunIfIdleAsync(CancellationToken.None);

        result.Skipped.Should().BeTrue();
    }

    [Test]
    public async Task ShareMetadataContract_ShouldPass_ForBothImplementations()
    {
        var root = Path.Combine(Path.GetTempPath(), $"shadowdrop-share-contract-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var options = new ShadowDropOptions
            {
                Metadata = new()
                {
                    LiteDbPath = Path.Combine(root, "metadata", "shadowdrop.db")
                }
            };
            using var liteDb = new LiteDbShareMetadataRepository(options);
            await AssertShareMetadataContractAsync(liteDb);
            await AssertShareMetadataContractAsync(_services.GetRequiredService<MongoShareMetadataRepository>());
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Test]
    public async Task ShareOperationClaims_ShouldAcquireMultiFileSetAtomicallyAndIdempotently()
    {
        var repository = _services.GetRequiredService<MongoShareOperationClaimRepository>();
        var operationId = Guid.NewGuid();
        var shareId = Guid.NewGuid();
        var fileIds = new[]
        {
            Guid.NewGuid(),
            Guid.NewGuid()
        };
        var acquired = await repository.TryAcquireAsync(operationId,
                                                        ShareOperationClaimKind.CreateShare,
                                                        shareId,
                                                        fileIds,
                                                        CancellationToken.None);
        var reacquired = await repository.TryAcquireAsync(operationId,
                                                          ShareOperationClaimKind.CreateShare,
                                                          shareId,
                                                          fileIds.Reverse().ToArray(),
                                                          CancellationToken.None);
        var conflict = await repository.TryAcquireAsync(Guid.NewGuid(),
                                                        ShareOperationClaimKind.CleanupShare,
                                                        Guid.NewGuid(),
                                                        [fileIds[1], Guid.NewGuid()],
                                                        CancellationToken.None);

        acquired.Should().NotBeNull();
        reacquired.Should().BeEquivalentTo(acquired);
        conflict.Should().BeNull();
        (await repository.TryReleaseAsync(operationId, CancellationToken.None)).Should().BeTrue();

        var sharedFileId = Guid.NewGuid();
        var attempts = new[]
        {
            repository.TryAcquireAsync(Guid.NewGuid(), ShareOperationClaimKind.CreateShare, Guid.NewGuid(), [sharedFileId], CancellationToken.None),
            repository.TryAcquireAsync(Guid.NewGuid(), ShareOperationClaimKind.CleanupShare, Guid.NewGuid(), [sharedFileId], CancellationToken.None)
        };
        var results = await Task.WhenAll(attempts);
        results.Count(claim => claim is not null).Should().Be(1);
        await repository.TryReleaseAsync(results.Single(claim => claim is not null)!.OperationId, CancellationToken.None);
    }

    [Test]
    public async Task ShareOperationClaims_ShouldFenceShareCreationAfterCleanupRunLeaseIsLost()
    {
        var claims = _services.GetRequiredService<MongoShareOperationClaimRepository>();
        var fileId = Guid.NewGuid();
        var cleanupShareId = Guid.NewGuid();

        // A run lease under its own name rather than the production one: this fixture also boots the real
        // application, whose startup cleanup can still hold `shadowdrop-share-cleanup` on its default lease.
        // What is under test is the claim outliving a lease, not which name the coordinator locks.
        var runLease = await _mongo.TryAcquireLockAsync($"cleanup-run-{Guid.NewGuid():N}", TimeSpan.FromMinutes(1));
        runLease.Should().NotBeNull();
        (await claims.TryAcquireAsync(cleanupShareId,
                                      ShareOperationClaimKind.CleanupShare,
                                      cleanupShareId,
                                      [fileId],
                                      CancellationToken.None)).Should().NotBeNull();

        // Losing the run lease is what would let a second instance start cleaning; the durable claim, not the
        // lease, is what keeps a concurrent share creation off a file whose blobs may still be deleted.
        await runLease.DisposeAsync();

        (await claims.TryAcquireAsync(Guid.NewGuid(),
                                      ShareOperationClaimKind.CreateShare,
                                      Guid.NewGuid(),
                                      [fileId],
                                      CancellationToken.None)).Should().BeNull();
        (await claims.TryReleaseAsync(cleanupShareId, CancellationToken.None)).Should().BeTrue();
    }

    [Test]
    public async Task ShareOperationClaims_ShouldPersistProposedShareThroughCommittingTransition()
    {
        var repository = _services.GetRequiredService<MongoShareOperationClaimRepository>();
        var operationId = Guid.NewGuid();
        var shareId = Guid.NewGuid();
        var fileId = Guid.NewGuid();

        // Nullable and nested members are populated on purpose: recovery replays this record verbatim, so the
        // serialized payload is the only thing an interrupted share creation can be resolved from.
        var proposed = new ShareRecord(shareId,
                                       $"claim-token-{Guid.NewGuid():N}",
                                       DateTimeOffset.Parse("2026-08-05T10:00:00Z"),
                                       DateTimeOffset.Parse("2026-08-06T10:00:00Z"),
                                       null,
                                       ShareCleanupState.Pending,
                                       false,
                                       new($"bearer-hash-{Guid.NewGuid():N}", DateTimeOffset.Parse("2026-08-05T11:00:00Z")),
                                       [new(fileId, "file.bin", "display.bin")],
                                       Guid.NewGuid());

        (await repository.TryAcquireAsync(operationId,
                                          ShareOperationClaimKind.CreateShare,
                                          shareId,
                                          [fileId],
                                          CancellationToken.None)).Should().NotBeNull();
        (await repository.TryBeginCommitAsync(operationId, proposed, CancellationToken.None)).Should().BeTrue();

        var recovered = (await repository.GetUnfinishedShareCreationsAsync([fileId], CancellationToken.None))
                        .Should().ContainSingle().Subject;
        recovered.Lifecycle.Should().Be(ShareOperationClaimLifecycle.Committing);
        recovered.ProposedShare.Should().BeEquivalentTo(proposed);
        (await repository.TryReleaseAsync(operationId, CancellationToken.None)).Should().BeTrue();
    }

    [Test]
    public async Task ShareRepository_ShouldComputeStatusCountsAndCleanupCandidatesServerSide()
    {
        var repository = _services.GetRequiredService<MongoShareMetadataRepository>();
        var now = DateTimeOffset.UtcNow;
        var baseline = await repository.GetStatusCountsAsync(now, CancellationToken.None);

        var activeId = Guid.NewGuid();
        var expiredId = Guid.NewGuid();
        var revokedId = Guid.NewGuid();
        var legacyCompletedId = Guid.NewGuid();
        var failedId = Guid.NewGuid();
        await repository.CreateAsync(CreateShare(activeId, $"counts-a-{Guid.NewGuid():N}", Guid.NewGuid()), CancellationToken.None);
        await repository.CreateAsync(
            CreateShare(expiredId, $"counts-e-{Guid.NewGuid():N}", Guid.NewGuid(), now.AddMinutes(-1)), CancellationToken.None);
        await repository.CreateAsync(CreateShare(revokedId, $"counts-r-{Guid.NewGuid():N}", Guid.NewGuid()), CancellationToken.None);
        await repository.CreateAsync(
            CreateShare(legacyCompletedId, $"counts-c-{Guid.NewGuid():N}", Guid.NewGuid(), now.AddMinutes(-1)), CancellationToken.None);
        await repository.CreateAsync(CreateShare(failedId, $"counts-f-{Guid.NewGuid():N}", Guid.NewGuid()), CancellationToken.None);
        (await repository.TryRevokeAsync(revokedId, now, CancellationToken.None)).Should().BeTrue();
        var shares = _services.GetRequiredService<IMongoHelper>().GetCollection<MongoShareDocument>();
        _ = await shares.UpdateOneAsync(document => document.ShareId == legacyCompletedId,
                                        Builders<MongoShareDocument>.Update.Set(document => document.CleanupState, "COMPLETED"));
        (await repository.TryRecordCleanupAttemptAsync(failedId, ShareCleanupState.Failed, now, [], CancellationToken.None)).Should().BeTrue();

        var counts = await repository.GetStatusCountsAsync(now, CancellationToken.None);
        counts.Active.Should().Be(baseline.Active + 2);
        counts.Expired.Should().Be(baseline.Expired + 2);
        counts.Revoked.Should().Be(baseline.Revoked + 1);
        counts.CleanupPending.Should().Be(baseline.CleanupPending + 4);
        counts.CleanupFailed.Should().Be(baseline.CleanupFailed + 1);

        var candidateIds = (await repository.GetCleanupCandidatesAsync(now, CancellationToken.None))
                           .Select(x => x.ShareId).ToHashSet();
        candidateIds.Should().Contain(expiredId);
        candidateIds.Should().Contain(revokedId);
        candidateIds.Should().NotContain(activeId);
        candidateIds.Should().Contain(legacyCompletedId, "legacy completed state is parsed as pending and purged after upgrade");
    }

    [Test]
    public async Task ShareRepository_ShouldEnforceTokenAndFileUniquenessAcrossConcurrentCreators()
    {
        var repository = _services.GetRequiredService<MongoShareMetadataRepository>();
        var fileId = Guid.NewGuid();
        var first = CreateShare(Guid.NewGuid(), "token-a", fileId);
        var second = CreateShare(Guid.NewGuid(), "token-b", fileId);
        var attempts = new[]
        {
            TryCreateAsync(repository, first),
            TryCreateAsync(repository, second)
        };
        var results = await Task.WhenAll(attempts);
        results.Count(x => x).Should().Be(1);
        (await repository.GetByShareTokenHashAsync(results[0] ? "token-a" : "token-b",
                                                   DateTimeOffset.UtcNow,
                                                   CancellationToken.None)).Should().NotBeNull();
    }

    [Test]
    public async Task ShareRepository_ShouldRevokeIdempotentlyAndRecordCleanupFailure()
    {
        var repository = _services.GetRequiredService<MongoShareMetadataRepository>();
        var shareId = Guid.NewGuid();
        await repository.CreateAsync(CreateShare(shareId, $"revoke-{Guid.NewGuid():N}", Guid.NewGuid()), CancellationToken.None);
        var firstRevokedAt = DateTimeOffset.UtcNow;

        (await repository.TryRevokeAsync(shareId, firstRevokedAt, CancellationToken.None)).Should().BeTrue();
        (await repository.TryRevokeAsync(shareId, firstRevokedAt.AddMinutes(5), CancellationToken.None)).Should().BeTrue();
        (await repository.TryRevokeAsync(Guid.NewGuid(), firstRevokedAt, CancellationToken.None)).Should().BeFalse();

        var revoked = await repository.GetAsync(shareId, CancellationToken.None);
        revoked!.RevokedAtUtc.Should().Be(
            DateTimeOffset.FromUnixTimeMilliseconds(firstRevokedAt.ToUnixTimeMilliseconds()),
            "the first revocation timestamp must win");

        (await repository.TryRecordCleanupAttemptAsync(shareId, ShareCleanupState.Failed, firstRevokedAt, [], CancellationToken.None)).Should().BeTrue();
        (await repository.TryRecordCleanupAttemptAsync(Guid.NewGuid(), ShareCleanupState.Failed, firstRevokedAt, [], CancellationToken.None)).Should()
            .BeFalse();
        (await repository.GetAsync(shareId, CancellationToken.None))!.CleanupState.Should().Be(ShareCleanupState.Failed);
    }

    [OneTimeSetUp]
    public async Task StartMongoAsync()
    {
        var builder = new MongoDbBuilder()
                      .WithImage(MongoImage)
                      // Work around the TCMalloc rseq crash affecting MongoDB 8.x on Linux 6.19+ hosts by letting glibc
                      // register rseq first. Remove this when SERVER-121912 is fixed in every tested MongoDB image.
                      // Source: https://jira.mongodb.org/browse/SERVER-121912
                      .WithEnvironment("GLIBC_TUNABLES", "glibc.pthread.rseq=1");
        if (UseReplicaSet)
        {
            builder = builder.WithReplicaSet();
        }

        _container = builder.Build();
        await _container.StartAsync();

        var options = new ShadowDropOptions
        {
            Metadata = new()
            {
                Provider = MetadataProvider.MongoDb
            },
            Storage = new()
            {
                Provider = BlobStorageProvider.MongoGridFs,
                GridFsBucketName = "shadowdrop_test_blobs"
            },
            Mongo = new()
            {
                ConnectionString = _container.GetConnectionString(),
                DatabaseName = $"shadowdrop_{Guid.NewGuid():N}"
            }
        };
        MongoSerialization.EnsureConfigured();
        _services = CreateMongoServiceProvider(options);
        _mongo = _services.GetRequiredService<IMongoHelper>();
        await _mongo.Database.RunCommandAsync<BsonDocument>(new BsonDocument("ping", 1));
        await _services.GetRequiredService<IMongoConfiguratorRunner>().RunConfiguratorsAsync();
    }

    [OneTimeTearDown]
    public async Task StopMongoAsync()
    {
        if (_services is not null)
        {
            await _mongo.Database.Client.DropDatabaseAsync(_mongo.Database.DatabaseNamespace.DatabaseName);
            await _services.DisposeAsync();
        }

        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    [Test]
    public async Task UploadCredentialRepository_ShouldEnforceSelectorDigestUniquenessAcrossConcurrentCreators()
    {
        var repository = _services.GetRequiredService<MongoUploadCredentialRepository>();
        var template = UploadCredentialRepositoryContract.CreateRecord(DateTimeOffset.UtcNow);
        var attempts = await Task.WhenAll(
            repository.TryCreateAsync(template, CancellationToken.None),
            repository.TryCreateAsync(template with
            {
                CredentialId = Guid.NewGuid()
            }, CancellationToken.None));

        attempts.Count(x => x).Should().Be(1);
    }

    [Test]
    public async Task UploadCredentialRepository_ShouldExposeRevocationToOtherInstancesImmediately()
    {
        var writer = _services.GetRequiredService<MongoUploadCredentialRepository>();
        var otherInstance = new MongoUploadCredentialRepository(_mongo);
        var record = UploadCredentialRepositoryContract.CreateRecord(DateTimeOffset.UtcNow);
        (await writer.TryCreateAsync(record, CancellationToken.None)).Should().BeTrue();
        var revokedAt = DateTimeOffset.FromUnixTimeMilliseconds(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        (await writer.RevokeAsync(record.CredentialId, revokedAt, CancellationToken.None)).Should().NotBeNull();

        var observed = await otherInstance.FindBySelectorDigestAsync(record.SelectorDigestBase64, CancellationToken.None);
        observed!.RevokedAtUtc.Should().Be(revokedAt, "revocation must be observed by every API instance sharing MongoDB");
    }

    [Test]
    public async Task UploadCredentialRepository_ShouldKeepLastUsedMonotonicAcrossConcurrentUpdates()
    {
        var repository = _services.GetRequiredService<MongoUploadCredentialRepository>();
        var record = UploadCredentialRepositoryContract.CreateRecord(DateTimeOffset.UtcNow);
        (await repository.TryCreateAsync(record, CancellationToken.None)).Should().BeTrue();

        var baseline = DateTimeOffset.FromUnixTimeMilliseconds(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        var timestamps = Enumerable.Range(0, 8).Select(offset => baseline.AddSeconds(offset)).ToArray();
        Random.Shared.Shuffle(timestamps);
        await Task.WhenAll(timestamps.Select(timestamp => repository.RecordUsageAsync(record.CredentialId, timestamp, CancellationToken.None)));

        (await repository.GetAsync(record.CredentialId, CancellationToken.None))!.LastUsedAtUtc
                                                                                 .Should().Be(baseline.AddSeconds(7),
                                                                                              "concurrent updates must never overwrite newer activity");
    }

    [Test]
    public async Task UploadCredentialRepository_ShouldPassSharedContracts()
    {
        var repository = _services.GetRequiredService<MongoUploadCredentialRepository>();

        await UploadCredentialRepositoryContract.AssertContractAsync(repository);
        await UploadCredentialRepositoryContract.AssertListPaginationContractAsync(repository);
    }

    [Test]
    public async Task UploadSweep_ShouldApplyDeterministicOrderingBeforeTheMongoCandidateLimit()
    {
        await using var scope = await CreateIsolatedMongoScopeAsync();
        var completedAt = DateTimeOffset.UtcNow.AddYears(-2);
        var documents = Enumerable.Range(0, UploadSweepService.MaxCandidatesPerRun + 25)
                                  .Select(index =>
                                  {
                                      var fileId = Guid.NewGuid();
                                      return new MongoUploadedFileDocument
                                      {
                                          FileId = fileId,
                                          BlobKey = $"batch/{fileId:N}",
                                          IsReserved = false,
                                          RetentionState = BlobRetentionState.Retained,
                                          CompletedAtUnixTimeMilliseconds = completedAt
                                                                            .AddMilliseconds(UploadSweepService.MaxCandidatesPerRun + 25 - index)
                                                                            .ToUnixTimeMilliseconds()
                                      };
                                  })
                                  .ToList();
        await scope.Mongo.GetCollection<MongoUploadedFileDocument>().InsertManyAsync(documents);

        var candidates = await scope.Services.GetRequiredService<MongoUploadedFileMetadataRepository>()
                                    .GetSweepCandidatesAsync(DateTimeOffset.UtcNow,
                                                             UploadSweepService.MaxCandidatesPerRun,
                                                             CancellationToken.None);

        candidates.Select(candidate => candidate.FileId).Should().Equal(
            documents.OrderBy(document => document.CompletedAtUnixTimeMilliseconds)
                     .Take(UploadSweepService.MaxCandidatesPerRun)
                     .Select(document => document.FileId));
    }

    [Test]
    public async Task UploadSweep_ShouldOrderCandidatesNeverInspectedFirstThenLeastRecentlyInspected()
    {
        var uploads = _services.GetRequiredService<MongoUploadedFileMetadataRepository>();
        var blobs = _services.GetRequiredService<MongoGridFsBlobStorage>();
        var neverInspected = await CompleteGridFsUploadAsync(uploads, blobs);
        var longAgoInspected = await CompleteGridFsUploadAsync(uploads, blobs);
        var recentlyInspected = await CompleteGridFsUploadAsync(uploads, blobs);
        var inspectedAt = DateTimeOffset.UtcNow;
        (await uploads.TryRecordSweepInspectionAsync(longAgoInspected, inspectedAt.AddDays(-2), CancellationToken.None))
            .Should().BeTrue();
        (await uploads.TryRecordSweepInspectionAsync(recentlyInspected, inspectedAt.AddDays(-1), CancellationToken.None))
            .Should().BeTrue();

        var candidates = await uploads.GetSweepCandidatesAsync(DateTimeOffset.UtcNow, 500, CancellationToken.None);

        // A missing inspection timestamp sorts before every number in MongoDB, so the never-inspected upload leads.
        // Other tests in this fixture leave candidates of their own behind, which is why only the relative order of
        // these three is asserted.
        candidates.Select(candidate => candidate.FileId)
                  .Where(fileId => fileId == neverInspected || fileId == longAgoInspected || fileId == recentlyInspected)
                  .Should().Equal(neverInspected, longAgoInspected, recentlyInspected);
    }

    [Test]
    public async Task UploadSweep_ShouldReclaimOnlyUnreferencedUploadsPastTheGracePeriod()
    {
        var uploads = _services.GetRequiredService<MongoUploadedFileMetadataRepository>();
        var shares = _services.GetRequiredService<MongoShareMetadataRepository>();
        var blobs = _services.GetRequiredService<MongoGridFsBlobStorage>();
        var unreferenced = await CompleteGridFsUploadAsync(uploads, blobs);
        var referenced = await CompleteGridFsUploadAsync(uploads, blobs);
        var recent = await CompleteGridFsUploadAsync(uploads, blobs);
        await shares.CreateAsync(CreateShare(Guid.NewGuid(), $"sweep-{Guid.NewGuid():N}", referenced), CancellationToken.None);

        // Only the back-dated uploads cross a one-year grace period, which also keeps this run from touching
        // uploads other tests in this fixture left behind.
        await BackdateCompletionAsync(unreferenced);
        await BackdateCompletionAsync(referenced);
        var sweep = CreateSweepService(OptionsWithRetention(TimeSpan.FromDays(365)));

        var result = await sweep.RunAsync(CancellationToken.None);

        result.Failures.Should().Be(0);
        (await uploads.GetAsync(unreferenced, CancellationToken.None)).Should().BeNull();
        await AssertGridFsUploadWasRemovedAsync(unreferenced);
        (await uploads.GetAsync(referenced, CancellationToken.None)).Should().NotBeNull("a referenced upload is never reclaimed");
        (await uploads.GetAsync(recent, CancellationToken.None)).Should().NotBeNull("an upload inside the grace period is never reclaimed");

        // Nothing may keep the referenced file claimed once the sweep has skipped it.
        var probe = Guid.NewGuid();
        (await _services.GetRequiredService<MongoShareOperationClaimRepository>()
                        .TryAcquireAsync(probe, ShareOperationClaimKind.CreateShare, Guid.NewGuid(), [referenced], CancellationToken.None))
            .Should().NotBeNull();
        (await _services.GetRequiredService<MongoShareOperationClaimRepository>().TryReleaseAsync(probe, CancellationToken.None))
            .Should().BeTrue();
    }

    [Test]
    public async Task UploadSweep_ShouldRotateRetainedMongoClaimsBeforeRecoveringALaterOrphan()
    {
        await using var scope = await CreateIsolatedMongoScopeAsync();
        var now = DateTimeOffset.UtcNow;
        var retainedFiles = Enumerable.Range(0, UploadSweepService.MaxRecoveryClaimsPerRun)
                                      .Select(_ => Guid.NewGuid())
                                      .ToList();
        await scope.Mongo.GetCollection<MongoUploadedFileDocument>().InsertManyAsync(
            retainedFiles.Select(fileId => new MongoUploadedFileDocument
            {
                FileId = fileId,
                BlobKey = $"retained/{fileId:N}",
                IsReserved = false,
                RetentionState = BlobRetentionState.Retained,
                CompletedAtUnixTimeMilliseconds = now.ToUnixTimeMilliseconds()
            }));

        var claims = scope.Services.GetRequiredService<MongoShareOperationClaimRepository>();
        var retainedOperationIds = new List<Guid>();
        foreach (var fileId in retainedFiles)
        {
            var operationId = Guid.NewGuid();
            retainedOperationIds.Add(operationId);
            (await claims.TryAcquireAsync(operationId,
                                          ShareOperationClaimKind.SweepUpload,
                                          operationId,
                                          [fileId],
                                          CancellationToken.None)).Should().NotBeNull();
        }

        var orphanedOperationId = Guid.NewGuid();
        (await claims.TryAcquireAsync(orphanedOperationId,
                                      ShareOperationClaimKind.SweepUpload,
                                      orphanedOperationId,
                                      [Guid.NewGuid()],
                                      CancellationToken.None)).Should().NotBeNull();
        (await claims.TryRecordSweepClaimInspectionAsync(orphanedOperationId, now.AddDays(-1), CancellationToken.None))
            .Should().BeTrue();

        var sweep = CreateSweepService(scope.Services, OptionsWithRetention(TimeSpan.FromDays(365)));
        var first = await sweep.RunAsync(CancellationToken.None);
        var afterFirst = await claims.GetSweepClaimsAsync(1000, CancellationToken.None);
        var second = await sweep.RunAsync(CancellationToken.None);
        var afterSecond = await claims.GetSweepClaimsAsync(1000, CancellationToken.None);

        first.Should().Be(new UploadSweepResult(0, 0, 0, 0));
        afterFirst.Select(claim => claim.OperationId).Should().Contain(orphanedOperationId);
        afterFirst[0].OperationId.Should().Be(orphanedOperationId,
                                              "the 50 retained claims were rotated behind the older orphan");
        second.Should().Be(new UploadSweepResult(0, 0, 0, 0));
        afterSecond.Select(claim => claim.OperationId).Should().BeEquivalentTo(retainedOperationIds);
    }

    [Test]
    public async Task UploadSweep_ShouldStampLegacyCompletion_AndWaitAFullGracePeriodFromThere()
    {
        var uploads = _services.GetRequiredService<MongoUploadedFileMetadataRepository>();
        var fileId = await CompleteGridFsUploadAsync(uploads, _services.GetRequiredService<MongoGridFsBlobStorage>());

        // A document written before completion timestamps existed carries no field at all rather than a null one,
        // because MongoUploadedFileDocument omits the property when it is null.
        await ClearCompletionAsync(fileId);
        var sweep = CreateSweepService(OptionsWithRetention(TimeSpan.FromDays(365)));

        // Each run's outcome is captured before the next one starts: the record only survives until the run that
        // legitimately reclaims it, so asserting afterwards would assert against the wrong phase.
        var stamping = await sweep.RunAsync(CancellationToken.None);
        var stampedCompletion = await GetCompletionAsync(fileId);
        var afterStamping = await uploads.GetAsync(fileId, CancellationToken.None);

        var withFreshStamp = await sweep.RunAsync(CancellationToken.None);
        var afterFreshStamp = await uploads.GetAsync(fileId, CancellationToken.None);

        await BackdateCompletionAsync(fileId);
        var reclaiming = await sweep.RunAsync(CancellationToken.None);

        // The legacy record surfaces despite its missing timestamp, is stamped on that first encounter, and is
        // never reclaimed merely for having carried no timestamp.
        stamping.Failures.Should().Be(0);
        stampedCompletion.Should().NotBeNull("the first inspection stamps a legacy record");
        afterStamping.Should().NotBeNull("the encounter that stamps a legacy record never reclaims it");

        // The stamp starts the grace period rather than ending it, so the very next run leaves the record alone.
        withFreshStamp.Failures.Should().Be(0);
        afterFreshStamp.Should().NotBeNull("a freshly stamped record waits a full grace period from that stamp");

        // Only once the stamp itself has aged past the retention does the record become eligible.
        reclaiming.Failures.Should().Be(0);
        (await uploads.GetAsync(fileId, CancellationToken.None)).Should().BeNull();
        await AssertGridFsUploadWasRemovedAsync(fileId);
    }

    [Test]
    public async Task UploadedFileMetadataContract_ShouldPass_ForBothImplementations()
    {
        var root = Path.Combine(Path.GetTempPath(), $"shadowdrop-upload-contract-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var options = new ShadowDropOptions
            {
                Metadata = new()
                {
                    LiteDbPath = Path.Combine(root, "metadata", "shadowdrop.db")
                }
            };
            var loggerFactory = _services.GetRequiredService<ILoggerFactory>();
            using var liteDb = new LiteDbUploadedFileMetadataRepository(
                options, loggerFactory.CreateLogger<LiteDbUploadedFileMetadataRepository>());
            await AssertUploadedFileMetadataContractAsync(liteDb);
            await AssertUploadedFileMetadataContractAsync(_services.GetRequiredService<MongoUploadedFileMetadataRepository>());
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Test]
    public async Task UploadedFileRepository_ShouldBindReservationAndCompletionToOwner()
    {
        var repository = _services.GetRequiredService<MongoUploadedFileMetadataRepository>();
        var ownerCredentialId = Guid.NewGuid();
        var foreignCredentialId = Guid.NewGuid();
        var fileId = await repository.ReserveFileIdAsync(ownerCredentialId, CancellationToken.None);

        (await repository.TryClaimReservationAsync(fileId, foreignCredentialId, CancellationToken.None)).Should().BeFalse();
        (await repository.TryClaimReservationAsync(fileId, ownerCredentialId, CancellationToken.None)).Should().BeTrue();
        (await repository.TryCompleteReservationAsync(
            CreateUploadedFile(fileId, $"owned-{fileId:N}", 4) with
            {
                OwnerCredentialId = foreignCredentialId
            },
            CancellationToken.None)).Should().BeFalse();
        (await repository.TryCompleteReservationAsync(
            CreateUploadedFile(fileId, $"owned-{fileId:N}", 4) with
            {
                OwnerCredentialId = ownerCredentialId
            },
            CancellationToken.None)).Should().BeTrue();

        var stored = await repository.GetAsync(fileId, CancellationToken.None);
        stored!.OwnerCredentialId.Should().Be(ownerCredentialId);
    }

    [Test]
    public async Task UploadedFileRepository_ShouldEnforceAtomicReservationLifecycle()
    {
        var repository = _services.GetRequiredService<MongoUploadedFileMetadataRepository>();
        var fileId = await repository.ReserveFileIdAsync(CancellationToken.None);
        var claims = await Task.WhenAll(Enumerable.Range(0, 8)
                                                  .Select(_ => repository.TryClaimReservationAsync(fileId, CancellationToken.None)));
        claims.Count(x => x).Should().Be(1);

        var record = CreateUploadedFile(fileId, fileId.ToString("N"), 37);
        (await repository.TryCompleteReservationAsync(record, CancellationToken.None)).Should().BeTrue();
        (await repository.GetAsync(fileId, CancellationToken.None)).Should().Be(record);
        (await repository.GetStorageStatsAsync(CancellationToken.None)).TotalEncryptedBytes.Should().BeGreaterThanOrEqualTo(37);
    }

    [Test]
    public async Task UploadedFileRepository_ShouldReleaseClaimsAndCountPendingReservations()
    {
        var repository = _services.GetRequiredService<MongoUploadedFileMetadataRepository>();
        var baseline = await repository.GetActivePendingReservationCountAsync(CancellationToken.None);

        var fileId = await repository.ReserveFileIdAsync(CancellationToken.None);
        (await repository.GetActivePendingReservationCountAsync(CancellationToken.None)).Should().Be(baseline + 1);

        (await repository.TryClaimReservationAsync(fileId, CancellationToken.None)).Should().BeTrue();
        (await repository.GetActivePendingReservationCountAsync(CancellationToken.None)).Should().Be(baseline);

        await repository.ReleaseClaimAsync(fileId, CancellationToken.None);
        (await repository.GetActivePendingReservationCountAsync(CancellationToken.None)).Should().Be(baseline + 1);
        (await repository.TryClaimReservationAsync(fileId, CancellationToken.None)).Should().BeTrue();
    }

    [Test]
    public async Task UploadedFileRepository_ShouldTreatMissingRetentionStateAsUnknown_AndReconcileItAtomically()
    {
        var repository = _services.GetRequiredService<MongoUploadedFileMetadataRepository>();
        var fileId = await repository.ReserveFileIdAsync(CancellationToken.None);
        (await repository.TryClaimReservationAsync(fileId, CancellationToken.None)).Should().BeTrue();
        (await repository.TryCompleteReservationAsync(CreateUploadedFile(fileId, fileId.ToString("N"), 37), CancellationToken.None)).Should().BeTrue();
        var collection = _services.GetRequiredService<IMongoHelper>().GetCollection<MongoUploadedFileDocument>();
        _ = await collection.UpdateOneAsync(document => document.FileId == fileId,
                                            Builders<MongoUploadedFileDocument>.Update.Unset(document => document.RetentionState));

        var unknownStats = await repository.GetStorageStatsAsync(CancellationToken.None);

        unknownStats.Should().Be(new UploadedFileStorageStats(null, null, false));
        (await repository.GetAsync(fileId, CancellationToken.None))!.RetentionState.Should().Be(BlobRetentionState.Unknown);
        (await repository.TryMarkBlobDeletedAsync(fileId, CancellationToken.None)).Should().BeTrue();
        (await repository.TryMarkBlobDeletedAsync(fileId, CancellationToken.None)).Should().BeTrue();
        (await repository.GetAsync(fileId, CancellationToken.None))!.RetentionState.Should().Be(BlobRetentionState.Deleted);
    }

    private static async Task AssertShareMetadataContractAsync(IShareMetadataRepository repository)
    {
        var now = DateTimeOffset.FromUnixTimeMilliseconds(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        var baseline = await repository.GetStatusCountsAsync(now, CancellationToken.None);
        var shareId = Guid.NewGuid();
        var token = $"contract-{Guid.NewGuid():N}";
        var fileId = Guid.NewGuid();
        var ownerCredentialId = Guid.NewGuid();
        var record = new ShareRecord(
            shareId, token, now, now.AddMinutes(-1), null, ShareCleanupState.Pending, false, null,
            [new(fileId, "file.bin", null)], ownerCredentialId);

        await repository.CreateAsync(record, CancellationToken.None);
        await repository.CreateAsync(record, CancellationToken.None);
        (await repository.GetAsync(shareId, CancellationToken.None)).Should().BeEquivalentTo(record);
        (await repository.GetByShareTokenHashAsync(token, now, CancellationToken.None)).Should().BeNull();
        (await repository.GetCleanupCandidatesAsync(now, CancellationToken.None)).Select(x => x.ShareId).Should().Contain(shareId);
        (await repository.GetStatusCountsAsync(now, CancellationToken.None)).Expired.Should().Be(baseline.Expired + 1);
        (await repository.TryRevokeAsync(shareId, now, CancellationToken.None)).Should().BeTrue();
        (await repository.TryRecordCleanupAttemptAsync(shareId, ShareCleanupState.Failed, now, [], CancellationToken.None)).Should().BeTrue();
        (await repository.GetAsync(shareId, CancellationToken.None))!.CleanupState.Should().Be(ShareCleanupState.Failed);

        var duplicateFile = CreateShare(Guid.NewGuid(), $"contract-{Guid.NewGuid():N}", fileId);
        var createDuplicate = async () => await repository.CreateAsync(duplicateFile, CancellationToken.None);
        await createDuplicate.Should().ThrowAsync<CreateShareValidationException>();

        var tiedIds = new[]
        {
            Guid.Parse("00000000-0000-0000-0000-000000000001"),
            Guid.Parse("80000000-0000-0000-0000-000000000001"),
            Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff")
        };
        var overlappingFilters = new[]
        {
            ShareListStatuses.Active,
            ShareListStatuses.CleanupPending
        };
        var baselineOverlapping = await repository.CountMatchingAsync(new(now, overlappingFilters, 1, null), CancellationToken.None);

        // Create the group far enough in the future to be unambiguously newest, so cursor paging over it is
        // deterministic no matter what the rest of the shared fixture already holds.
        var tiedCreatedAt = now.AddDays(30);
        foreach (var tiedId in tiedIds)
        {
            await repository.CreateAsync(new(tiedId,
                                             $"ordering-{Guid.NewGuid():N}",
                                             tiedCreatedAt,
                                             now.AddDays(60),
                                             null,
                                             ShareCleanupState.Pending,
                                             false,
                                             null,
                                             [new(Guid.NewGuid(), "private-name.bin", null)]),
                                         CancellationToken.None);
        }

        var expectedOrder = tiedIds.OrderByDescending(id => id.ToString("D"), StringComparer.Ordinal).ToArray();
        var listed = await repository.GetListPageAsync(new(now, [], 20, null), CancellationToken.None);
        listed.Shares.Where(share => tiedIds.Contains(share.ShareId)).Select(share => share.ShareId).Should().Equal(expectedOrder);

        // Continuation must resume strictly after the last (CreatedAtUtc, ShareId) pair, including part-way through
        // an equal-timestamp group, and must not repeat or skip a share.
        var firstPage = await repository.GetListPageAsync(new(now, [], 2, null), CancellationToken.None);
        var secondPage = await repository.GetListPageAsync(new(now, [], 2, firstPage.NextCursor), CancellationToken.None);
        firstPage.Shares.Select(share => share.ShareId).Should().Equal(expectedOrder[..2]);
        firstPage.NextCursor.Should().NotBeNull();
        secondPage.Shares.Select(share => share.ShareId).First().Should().Be(expectedOrder[2]);

        // Every tied share matches both filters, so an OR-combined query has to count each of them exactly once.
        (await repository.CountMatchingAsync(new(now, overlappingFilters, 1, null), CancellationToken.None))
            .Should().Be(baselineOverlapping + tiedIds.Length);

        // The exact total is independent of page size and cursor position.
        var unpagedTotal = await repository.CountMatchingAsync(new(now, [], 200, null), CancellationToken.None);
        (await repository.CountMatchingAsync(new(now, [], 2, firstPage.NextCursor), CancellationToken.None)).Should().Be(unpagedTotal);
        (await repository.CountMatchingAsync(new(now, [], 1, null), CancellationToken.None)).Should().Be(unpagedTotal);
    }

    private static async Task AssertUploadedFileMetadataContractAsync(IUploadedFileMetadataRepository repository)
    {
        var baselinePending = await repository.GetActivePendingReservationCountAsync(CancellationToken.None);
        var baselineStats = await repository.GetStorageStatsAsync(CancellationToken.None);
        baselineStats.IsExact.Should().BeTrue();
        var fileId = await repository.ReserveFileIdAsync(CancellationToken.None);
        (await repository.GetActivePendingReservationCountAsync(CancellationToken.None)).Should().Be(baselinePending + 1);
        (await repository.GetAsync(fileId, CancellationToken.None)).Should().BeNull();

        (await repository.TryClaimReservationAsync(fileId, CancellationToken.None)).Should().BeTrue();
        (await repository.TryClaimReservationAsync(fileId, CancellationToken.None)).Should().BeFalse();
        await repository.ReleaseClaimAsync(fileId, CancellationToken.None);
        (await repository.TryClaimReservationAsync(fileId, CancellationToken.None)).Should().BeTrue();

        var record = CreateUploadedFile(fileId, fileId.ToString("N"), 37);
        (await repository.TryCompleteReservationAsync(record, CancellationToken.None)).Should().BeTrue();
        (await repository.GetAsync(fileId, CancellationToken.None)).Should().Be(record);
        (await repository.GetListProjectionsAsync([fileId, Guid.NewGuid()], CancellationToken.None)).Should().Equal(
            new UploadedFileListProjection(fileId, 37, BlobRetentionState.Retained));
        var stats = await repository.GetStorageStatsAsync(CancellationToken.None);
        stats.CompletedFileCount.Should().Be(baselineStats.CompletedFileCount + 1);
        stats.TotalEncryptedBytes.Should().Be(baselineStats.TotalEncryptedBytes + 37);
        (await repository.TryMarkBlobDeletedAsync(fileId, CancellationToken.None)).Should().BeTrue();
        (await repository.TryMarkBlobDeletedAsync(fileId, CancellationToken.None)).Should().BeTrue();
        var afterDelete = await repository.GetStorageStatsAsync(CancellationToken.None);
        afterDelete.CompletedFileCount.Should().Be(baselineStats.CompletedFileCount);
        afterDelete.TotalEncryptedBytes.Should().Be(baselineStats.TotalEncryptedBytes);
    }

    private static async Task<Guid> CompleteGridFsUploadAsync(
        MongoUploadedFileMetadataRepository uploads,
        MongoGridFsBlobStorage blobs)
    {
        var fileId = await uploads.ReserveFileIdAsync(CancellationToken.None);
        (await uploads.TryClaimReservationAsync(fileId, CancellationToken.None)).Should().BeTrue();
        var descriptor = await blobs.SaveAsync(fileId, new MemoryStream([1, 2, 3, 4]), CancellationToken.None);
        (await uploads.TryCompleteReservationAsync(CreateUploadedFile(fileId, descriptor.BlobKey, descriptor.WrittenLength),
                                                   CancellationToken.None)).Should().BeTrue();
        return fileId;
    }

    private static ServiceProvider CreateMongoServiceProvider(ShadowDropOptions options)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(options);
        services.AddMongo(options.Mongo.ConnectionString, options.Mongo.DatabaseName, mongoOptions =>
                {
                    mongoOptions.UseDefaultCollectionNames = false;
                    mongoOptions.AddMapping<MongoUploadedFileDocument>("uploaded_files");
                    mongoOptions.AddMapping<MongoShareDocument>("shares");
                    mongoOptions.AddMapping<MongoShareOperationClaimDocument>("share_operation_claims");
                    mongoOptions.AddMapping<MongoAdminTokenCredentialDocument>("admin_tokens");
                    mongoOptions.AddMapping<MongoUploadCredentialDocument>("upload_credentials");
                })
                .WithConfigurator<ShadowDropMongoConfigurator>();
        services.AddSingleton<MongoUploadedFileMetadataRepository>();
        services.AddSingleton<MongoShareMetadataRepository>();
        services.AddSingleton<MongoShareOperationClaimRepository>();
        services.AddSingleton<MongoAdminTokenCredentialRepository>();
        services.AddSingleton<MongoUploadCredentialRepository>();
        services.AddSingleton<MongoGridFsBlobStorage>();
        return services.BuildServiceProvider();
    }

    private static ShareRecord CreateShare(Guid shareId, String token, Guid fileId, DateTimeOffset? expiresAtUtc = null) =>
        new(shareId, token, DateTimeOffset.UtcNow, expiresAtUtc ?? DateTimeOffset.UtcNow.AddHours(1), null, ShareCleanupState.Pending,
            false, null, [new(fileId, "file.bin", null)]);

    private static UploadSweepService CreateSweepService(IServiceProvider services, ShadowDropOptions? options = null)
    {
        var shares = services.GetRequiredService<MongoShareMetadataRepository>();
        var claims = services.GetRequiredService<MongoShareOperationClaimRepository>();
        var loggerFactory = services.GetRequiredService<ILoggerFactory>();
        return new(services.GetRequiredService<MongoUploadedFileMetadataRepository>(),
                   shares,
                   claims,
                   new(claims, shares, loggerFactory.CreateLogger<ShareCreationClaimReconciler>()),
                   services.GetRequiredService<MongoGridFsBlobStorage>(),
                   options ?? new(),
                   TimeProvider.System,
                   loggerFactory.CreateLogger<UploadSweepService>());
    }

    private static UploadedFileRecord CreateUploadedFile(Guid fileId, String blobKey, Int64 length) =>
        new(fileId, blobKey, "file.bin", length, length, "application/octet-stream", "v2", "aes", 1024, 1, "salt", null);

    private static ShadowDropOptions OptionsWithRetention(TimeSpan retention) =>
        new()
        {
            Cleanup = new()
            {
                UnreferencedUploadRetention = retention
            }
        };

    private static async Task<Boolean> TryCreateAsync(MongoShareMetadataRepository repository, ShareRecord record)
    {
        try
        {
            await repository.CreateAsync(record, CancellationToken.None);
            return true;
        }
        catch (CreateShareValidationException)
        {
            return false;
        }
    }

    private async Task AssertGridFsUploadWasRemovedAsync(Guid fileId)
    {
        var fileCount = await _mongo.Database.GetCollection<BsonDocument>("shadowdrop_test_blobs.files")
                                    .CountDocumentsAsync(Builders<BsonDocument>.Filter.Eq("_id", fileId));
        var chunkCount = await _mongo.Database.GetCollection<BsonDocument>("shadowdrop_test_blobs.chunks")
                                     .CountDocumentsAsync(Builders<BsonDocument>.Filter.Eq("files_id", fileId));
        fileCount.Should().Be(0);
        chunkCount.Should().Be(0);
    }

    private Task BackdateCompletionAsync(Guid fileId) =>
        _mongo.GetCollection<MongoUploadedFileDocument>()
              .UpdateOneAsync(x => x.FileId == fileId,
                              Builders<MongoUploadedFileDocument>.Update
                                                                 .Set(x => x.CompletedAtUnixTimeMilliseconds,
                                                                      DateTimeOffset.UtcNow.AddYears(-2).ToUnixTimeMilliseconds()));

    /// <summary>Reproduces a document written before completion timestamps existed: the field is absent entirely.</summary>
    private Task ClearCompletionAsync(Guid fileId) =>
        _mongo.GetCollection<MongoUploadedFileDocument>()
              .UpdateOneAsync(x => x.FileId == fileId,
                              Builders<MongoUploadedFileDocument>.Update.Unset(x => x.CompletedAtUnixTimeMilliseconds));

    private async Task<IsolatedMongoScope> CreateIsolatedMongoScopeAsync()
    {
        var options = new ShadowDropOptions
        {
            Metadata = new()
            {
                Provider = MetadataProvider.MongoDb
            },
            Storage = new()
            {
                Provider = BlobStorageProvider.MongoGridFs,
                GridFsBucketName = "shadowdrop_test_blobs"
            },
            Mongo = new()
            {
                ConnectionString = _container.GetConnectionString(),
                DatabaseName = $"shadowdrop_sweep_{Guid.NewGuid():N}"
            }
        };
        var services = CreateMongoServiceProvider(options);
        var mongo = services.GetRequiredService<IMongoHelper>();
        await mongo.Database.RunCommandAsync<BsonDocument>(new BsonDocument("ping", 1));
        await services.GetRequiredService<IMongoConfiguratorRunner>().RunConfiguratorsAsync();
        return new(services, mongo);
    }

    private UploadSweepService CreateSweepService(ShadowDropOptions? options = null) =>
        CreateSweepService(_services, options);

    private async Task<Int64?> GetCompletionAsync(Guid fileId) =>
        (await _mongo.GetCollection<MongoUploadedFileDocument>()
                     .Find(x => x.FileId == fileId)
                     .FirstOrDefaultAsync())?.CompletedAtUnixTimeMilliseconds;

    private sealed class FailAfterStream(Byte[] content, Int32 throwAfter, Exception failure) : Stream
    {
        private Int32 _position;
        public override Boolean CanRead => true;
        public override Boolean CanSeek => false;
        public override Boolean CanWrite => false;
        public override Int64 Length => content.Length;
        public override Int64 Position { get => _position; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override Int32 Read(Byte[] buffer, Int32 offset, Int32 count) => throw new NotSupportedException();

        public override async ValueTask<Int32> ReadAsync(Memory<Byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            if (_position >= throwAfter)
            {
                throw failure;
            }

            var count = Math.Min(buffer.Length, Math.Min(throwAfter - _position, content.Length - _position));
            content.AsMemory(_position, count).CopyTo(buffer);
            _position += count;
            return count;
        }

        public override Int64 Seek(Int64 offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(Int64 value) => throw new NotSupportedException();
        public override void Write(Byte[] buffer, Int32 offset, Int32 count) => throw new NotSupportedException();
    }

    private sealed class IsolatedMongoScope(ServiceProvider services, IMongoHelper mongo) : IAsyncDisposable
    {
        public IMongoHelper Mongo { get; } = mongo;

        public ServiceProvider Services { get; } = services;

        public async ValueTask DisposeAsync()
        {
            await Mongo.Database.Client.DropDatabaseAsync(Mongo.Database.DatabaseNamespace.DatabaseName);
            await Services.DisposeAsync();
        }
    }

    // Program reads configuration overrides from environment variables (same mechanism as ApiWalkingSkeletonTests'
    // TestApiFactory); the fixture is [NonParallelizable], so mutating and restoring them per boot is safe.
    private sealed class ProviderMatrixApiFactory : WebApplicationFactory<Program>
    {
        private readonly Dictionary<String, String?> _previousValues = [];
        private readonly String _rootDirectory;

        public ProviderMatrixApiFactory(MetadataProvider metadataProvider,
                                        BlobStorageProvider blobProvider,
                                        String connectionString,
                                        String databaseName)
        {
            _rootDirectory = Path.Combine(Path.GetTempPath(), $"shadowdrop-app-smoke-{Guid.NewGuid():N}");
            Directory.CreateDirectory(_rootDirectory);
            SetEnvironmentVariable("ShadowDrop__Metadata__Provider", metadataProvider.ToString());
            SetEnvironmentVariable("ShadowDrop__Metadata__LiteDbPath", Path.Combine(_rootDirectory, "metadata", "shadowdrop.db"));
            SetEnvironmentVariable("ShadowDrop__Storage__Provider", blobProvider.ToString());
            SetEnvironmentVariable("ShadowDrop__Storage__LocalRoot", Path.Combine(_rootDirectory, "blobs"));
            SetEnvironmentVariable("ShadowDrop__Storage__GridFsBucketName", "shadowdrop_app_smoke_blobs");
            SetEnvironmentVariable("ShadowDrop__Mongo__ConnectionString", connectionString);
            SetEnvironmentVariable("ShadowDrop__Mongo__DatabaseName", databaseName);
            SetEnvironmentVariable("ShadowDrop__ApiExposure__EnableAdminOperations", "false");
            SetEnvironmentVariable("ShadowDrop__ApiExposure__EnablePublicDownloads", "true");
        }

        protected override void Dispose(Boolean disposing)
        {
            if (disposing)
            {
                foreach (var (key, value) in _previousValues)
                {
                    Environment.SetEnvironmentVariable(key, value);
                }
            }

            base.Dispose(disposing);

            if (Directory.Exists(_rootDirectory))
            {
                Directory.Delete(_rootDirectory, true);
            }
        }

        private void SetEnvironmentVariable(String key, String? value)
        {
            _previousValues[key] = Environment.GetEnvironmentVariable(key);
            Environment.SetEnvironmentVariable(key, value);
        }
    }
}
