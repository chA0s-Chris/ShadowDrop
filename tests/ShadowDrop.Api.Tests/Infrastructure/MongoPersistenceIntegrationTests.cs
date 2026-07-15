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
using ShadowDrop.Tests.Infrastructure.Security;
using System.Net;
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
            foreach (var blobProvider in Enum.GetValues<BlobStorageProvider>())
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
            foreach (var blobProvider in Enum.GetValues<BlobStorageProvider>())
            {
                await using var factory = new ProviderMatrixApiFactory(
                    metadataProvider, blobProvider,
                    _container.GetConnectionString(), _mongo.Database.DatabaseNamespace.DatabaseName);
                using var client = factory.CreateClient();

                var response = await client.GetAsync("/health/ready");

                response.StatusCode.Should().Be(
                    HttpStatusCode.OK,
                    $"the application must start and serve requests with {metadataProvider} metadata and {blobProvider} blobs");
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
            await AssertBlobStorageContractAsync(
                new LocalBlobStorage(options, loggerFactory.CreateLogger<LocalBlobStorage>()));
            await AssertBlobStorageContractAsync(_services.GetRequiredService<MongoGridFsBlobStorage>());
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
        uploadIndexes.Select(x => x["name"].AsString).Should().Contain(["reservation_state", "storage_stats"]);
        shareIndexes.Select(x => x["name"].AsString).Should().Contain(
            ["share_token_unique", "file_single_use", "cleanup_candidates"]);
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
            _services.GetRequiredService<MongoGridFsBlobStorage>(),
            TimeProvider.System,
            _services.GetRequiredService<ILoggerFactory>().CreateLogger<ShareCleanupService>());
        using var coordinator = new MongoShareCleanupCoordinator(_mongo);
        var runner = new ShareCleanupRunner(
            service, coordinator, _services.GetRequiredService<ILoggerFactory>().CreateLogger<ShareCleanupRunner>());
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
    public async Task ShareRepository_ShouldComputeStatusCountsAndCleanupCandidatesServerSide()
    {
        var repository = _services.GetRequiredService<MongoShareMetadataRepository>();
        var now = DateTimeOffset.UtcNow;
        var baseline = await repository.GetStatusCountsAsync(now, CancellationToken.None);

        var activeId = Guid.NewGuid();
        var expiredId = Guid.NewGuid();
        var revokedId = Guid.NewGuid();
        var completedId = Guid.NewGuid();
        var failedId = Guid.NewGuid();
        await repository.CreateAsync(CreateShare(activeId, $"counts-a-{Guid.NewGuid():N}", Guid.NewGuid()), CancellationToken.None);
        await repository.CreateAsync(
            CreateShare(expiredId, $"counts-e-{Guid.NewGuid():N}", Guid.NewGuid(), now.AddMinutes(-1)), CancellationToken.None);
        await repository.CreateAsync(CreateShare(revokedId, $"counts-r-{Guid.NewGuid():N}", Guid.NewGuid()), CancellationToken.None);
        await repository.CreateAsync(
            CreateShare(completedId, $"counts-c-{Guid.NewGuid():N}", Guid.NewGuid(), now.AddMinutes(-1)), CancellationToken.None);
        await repository.CreateAsync(CreateShare(failedId, $"counts-f-{Guid.NewGuid():N}", Guid.NewGuid()), CancellationToken.None);
        (await repository.TryRevokeAsync(revokedId, now, CancellationToken.None)).Should().BeTrue();
        (await repository.TryUpdateCleanupStateAsync(completedId, ShareCleanupState.Completed, CancellationToken.None)).Should().BeTrue();
        (await repository.TryUpdateCleanupStateAsync(failedId, ShareCleanupState.Failed, CancellationToken.None)).Should().BeTrue();

        var counts = await repository.GetStatusCountsAsync(now, CancellationToken.None);
        counts.Active.Should().Be(baseline.Active + 1);
        counts.Expired.Should().Be(baseline.Expired + 1);
        counts.Revoked.Should().Be(baseline.Revoked + 1);
        counts.CleanupCompleted.Should().Be(baseline.CleanupCompleted + 1);
        counts.CleanupFailed.Should().Be(baseline.CleanupFailed + 1);

        var candidateIds = (await repository.GetCleanupCandidatesAsync(now, CancellationToken.None))
                           .Select(x => x.ShareId).ToHashSet();
        candidateIds.Should().Contain(expiredId);
        candidateIds.Should().Contain(revokedId);
        candidateIds.Should().NotContain(activeId);
        candidateIds.Should().NotContain(completedId, "completed shares are no longer cleanup candidates even when expired");
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
        (await repository.GetByShareTokenHashAsync(results[0] ? "token-a" : "token-b", CancellationToken.None)).Should().NotBeNull();
    }

    [Test]
    public async Task ShareRepository_ShouldRevokeIdempotentlyAndUpdateCleanupState()
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

        (await repository.TryUpdateCleanupStateAsync(shareId, ShareCleanupState.Completed, CancellationToken.None)).Should().BeTrue();
        (await repository.TryUpdateCleanupStateAsync(Guid.NewGuid(), ShareCleanupState.Completed, CancellationToken.None)).Should().BeFalse();
        (await repository.GetAsync(shareId, CancellationToken.None))!.CleanupState.Should().Be(ShareCleanupState.Completed);
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
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(options);
        services.AddMongo(options.Mongo.ConnectionString, options.Mongo.DatabaseName, mongoOptions =>
                {
                    mongoOptions.UseDefaultCollectionNames = false;
                    mongoOptions.AddMapping<MongoUploadedFileDocument>("uploaded_files");
                    mongoOptions.AddMapping<MongoShareDocument>("shares");
                    mongoOptions.AddMapping<MongoAdminTokenCredentialDocument>("admin_tokens");
                    mongoOptions.AddMapping<MongoUploadCredentialDocument>("upload_credentials");
                })
                .WithConfigurator<ShadowDropMongoConfigurator>();
        services.AddSingleton<MongoUploadedFileMetadataRepository>();
        services.AddSingleton<MongoShareMetadataRepository>();
        services.AddSingleton<MongoAdminTokenCredentialRepository>();
        services.AddSingleton<MongoUploadCredentialRepository>();
        services.AddSingleton<MongoGridFsBlobStorage>();
        _services = services.BuildServiceProvider();
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

    private static async Task AssertBlobStorageContractAsync(IBlobStorage storage)
    {
        var fileId = Guid.NewGuid();
        Byte[] content = [1, 2, 3, 4, 5];
        var descriptor = await storage.SaveAsync(fileId, new MemoryStream(content), CancellationToken.None);
        descriptor.WrittenLength.Should().Be(content.Length);

        await using (var stream = await storage.OpenReadAsync(descriptor.BlobKey, CancellationToken.None))
        {
            using var copy = new MemoryStream();
            await stream.CopyToAsync(copy);
            copy.ToArray().Should().Equal(content);
        }

        (await storage.DeleteIfExistsAsync(descriptor.BlobKey, CancellationToken.None)).Should().BeTrue();
        (await storage.DeleteIfExistsAsync(descriptor.BlobKey, CancellationToken.None)).Should().BeFalse();
        var openMissing = async () => await storage.OpenReadAsync(descriptor.BlobKey, CancellationToken.None);
        await openMissing.Should().ThrowAsync<IOException>();
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
        (await repository.GetAsync(shareId, CancellationToken.None)).Should().BeEquivalentTo(record);
        (await repository.GetByShareTokenHashAsync(token, CancellationToken.None)).Should().BeEquivalentTo(record);
        (await repository.GetCleanupCandidatesAsync(now, CancellationToken.None)).Select(x => x.ShareId).Should().Contain(shareId);
        (await repository.GetStatusCountsAsync(now, CancellationToken.None)).Expired.Should().Be(baseline.Expired + 1);
        (await repository.TryRevokeAsync(shareId, now, CancellationToken.None)).Should().BeTrue();
        (await repository.TryUpdateCleanupStateAsync(shareId, ShareCleanupState.Completed, CancellationToken.None)).Should().BeTrue();
        (await repository.GetAsync(shareId, CancellationToken.None))!.CleanupState.Should().Be(ShareCleanupState.Completed);

        var duplicateFile = CreateShare(Guid.NewGuid(), $"contract-{Guid.NewGuid():N}", fileId);
        var createDuplicate = async () => await repository.CreateAsync(duplicateFile, CancellationToken.None);
        await createDuplicate.Should().ThrowAsync<CreateShareValidationException>();
    }

    private static async Task AssertUploadedFileMetadataContractAsync(IUploadedFileMetadataRepository repository)
    {
        var baselinePending = await repository.GetActivePendingReservationCountAsync(CancellationToken.None);
        var baselineStats = await repository.GetStorageStatsAsync(CancellationToken.None);
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
        var stats = await repository.GetStorageStatsAsync(CancellationToken.None);
        stats.CompletedFileCount.Should().Be(baselineStats.CompletedFileCount + 1);
        stats.TotalEncryptedBytes.Should().Be(baselineStats.TotalEncryptedBytes + 37);
    }

    private static ShareRecord CreateShare(Guid shareId, String token, Guid fileId, DateTimeOffset? expiresAtUtc = null) =>
        new(shareId, token, DateTimeOffset.UtcNow, expiresAtUtc ?? DateTimeOffset.UtcNow.AddHours(1), null, ShareCleanupState.Pending,
            false, null, [new(fileId, "file.bin", null)]);

    private static UploadedFileRecord CreateUploadedFile(Guid fileId, String blobKey, Int64 length) =>
        new(fileId, blobKey, "file.bin", length, length, "application/octet-stream", "v2", "aes", 1024, 1, "salt", null);

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
