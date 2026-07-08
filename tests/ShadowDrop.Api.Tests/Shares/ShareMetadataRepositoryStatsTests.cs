// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Shares;

using FluentAssertions;
using NUnit.Framework;
using ShadowDrop.Api.Configuration;
using ShadowDrop.Api.Shares;

public sealed class ShareMetadataRepositoryStatsTests
{
    [Test]
    public async Task GetStatusCountsAsync_ShouldClassifySharesByCleanupStateThenRevocationThenExpiration()
    {
        await using var fixture = new StatsFixture();
        var options = fixture.CreateOptions();
        using var repository = new LiteDbShareMetadataRepository(options);
        var now = DateTimeOffset.Parse("2026-06-01T00:00:00Z");

        await repository.CreateAsync(CreateShare(now.AddDays(1)), CancellationToken.None); // active
        var revokedShare = CreateShare(now.AddDays(1));
        await repository.CreateAsync(revokedShare, CancellationToken.None);
        (await repository.TryRevokeAsync(revokedShare.ShareId, now, CancellationToken.None)).Should().BeTrue(); // revoked
        await repository.CreateAsync(CreateShare(now.AddDays(-1)), CancellationToken.None); // expired
        var completedShare = CreateShare(now.AddDays(-1));
        await repository.CreateAsync(completedShare, CancellationToken.None);
        (await repository.TryUpdateCleanupStateAsync(completedShare.ShareId, ShareCleanupState.Completed, CancellationToken.None)).Should().BeTrue();
        var failedShare = CreateShare(now.AddDays(-1));
        await repository.CreateAsync(failedShare, CancellationToken.None);
        (await repository.TryUpdateCleanupStateAsync(failedShare.ShareId, ShareCleanupState.Failed, CancellationToken.None)).Should().BeTrue();

        var counts = await repository.GetStatusCountsAsync(now, CancellationToken.None);

        counts.Should().Be(new ShareStatusCounts(Active: 1, Expired: 1, Revoked: 1, CleanupCompleted: 1, CleanupFailed: 1));
    }

    private static ShareRecord CreateShare(DateTimeOffset expiresAtUtc) =>
        new(Guid.NewGuid(),
            $"share-token-hash-{Guid.NewGuid():N}",
            DateTimeOffset.Parse("2026-05-01T00:00:00Z"),
            expiresAtUtc,
            null,
            ShareCleanupState.Pending,
            DirectHttpEnabled: false,
            DownloadBearerToken: null,
            [new(Guid.NewGuid(), "cipher.bin", null)]);

    private sealed class StatsFixture : IAsyncDisposable
    {
        private readonly String _rootDirectory = Path.Combine(TestContext.CurrentContext.WorkDirectory,
                                                              "artifacts",
                                                              "share-stats-tests",
                                                              Guid.NewGuid().ToString("N"));

        public StatsFixture()
        {
            Directory.CreateDirectory(_rootDirectory);
        }

        public ShadowDropOptions CreateOptions() =>
            new()
            {
                Metadata = new()
                {
                    LiteDbPath = Path.Combine(_rootDirectory, "metadata", "shadowdrop.db")
                },
                Storage = new()
                {
                    LocalRoot = Path.Combine(_rootDirectory, "storage")
                }
            };

        public ValueTask DisposeAsync()
        {
            if (Directory.Exists(_rootDirectory))
            {
                Directory.Delete(_rootDirectory, true);
            }

            return ValueTask.CompletedTask;
        }
    }
}
