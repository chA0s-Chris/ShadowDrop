// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Shares;

using FluentAssertions;
using NUnit.Framework;
using ShadowDrop.Api.Configuration;
using ShadowDrop.Api.Shares;

public sealed class ShareOperationClaimRepositoryTests
{
    [Test]
    public async Task LiteDb_ShouldAbortOnlyBeforeCommittingStateIsWon()
    {
        await using var fixture = new ClaimFixture();
        using var repository = new LiteDbShareOperationClaimRepository(fixture.Options);
        var abandonedOperationId = Guid.NewGuid();
        var committingOperationId = Guid.NewGuid();
        var share = CreateShare();
        (await repository.TryAcquireAsync(abandonedOperationId,
                                          ShareOperationClaimKind.CreateShare,
                                          Guid.NewGuid(),
                                          [Guid.NewGuid()],
                                          CancellationToken.None)).Should().NotBeNull();
        (await repository.TryAcquireAsync(committingOperationId,
                                          ShareOperationClaimKind.CreateShare,
                                          share.ShareId,
                                          share.Files.Select(file => file.FileId).ToArray(),
                                          CancellationToken.None)).Should().NotBeNull();
        (await repository.TryBeginCommitAsync(committingOperationId, share, CancellationToken.None)).Should().BeTrue();

        (await repository.TryAbortAcquiredAsync(abandonedOperationId, CancellationToken.None)).Should().BeTrue();
        (await repository.TryAbortAcquiredAsync(committingOperationId, CancellationToken.None)).Should().BeFalse();
        (await repository.GetUnfinishedShareCreationsAsync(share.Files.Select(file => file.FileId).ToArray(), CancellationToken.None))
            .Should().ContainSingle(claim =>
                                        claim.OperationId == committingOperationId && claim.Lifecycle == ShareOperationClaimLifecycle.Committing);
    }

    [Test]
    public async Task LiteDb_ShouldAcquireMultiFileClaimAtomically_AndReacquireIdempotently()
    {
        await using var fixture = new ClaimFixture();
        using var repository = new LiteDbShareOperationClaimRepository(fixture.Options);
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
                                                          [.. fileIds.Reverse()],
                                                          CancellationToken.None);
        var conflict = await repository.TryAcquireAsync(Guid.NewGuid(),
                                                        ShareOperationClaimKind.CleanupShare,
                                                        Guid.NewGuid(),
                                                        [fileIds[1], Guid.NewGuid()],
                                                        CancellationToken.None);

        acquired.Should().NotBeNull();
        reacquired.Should().BeEquivalentTo(acquired);
        conflict.Should().BeNull();
    }

    [Test]
    public async Task LiteDb_ShouldAllowOnlyOneConcurrentOverlappingClaimAcrossRepositoryInstances()
    {
        await using var fixture = new ClaimFixture();
        using var firstRepository = new LiteDbShareOperationClaimRepository(fixture.Options);
        using var secondRepository = new LiteDbShareOperationClaimRepository(fixture.Options);
        var sharedFileId = Guid.NewGuid();
        using var start = new ManualResetEventSlim();

        Task<ShareOperationClaim?> AttemptAsync(
            IShareOperationClaimRepository repository,
            ShareOperationClaimKind kind)
        {
            return Task.Run(async () =>
            {
                start.Wait();
                return await repository.TryAcquireAsync(Guid.NewGuid(),
                                                        kind,
                                                        Guid.NewGuid(),
                                                        [sharedFileId],
                                                        CancellationToken.None);
            });
        }

        var attempts = new[]
        {
            AttemptAsync(firstRepository, ShareOperationClaimKind.CreateShare),
            AttemptAsync(secondRepository, ShareOperationClaimKind.CleanupShare)
        };
        start.Set();
        var results = await Task.WhenAll(attempts);

        results.Count(claim => claim is not null).Should().Be(1);
    }

    [Test]
    public async Task LiteDb_ShouldPersistProposedShareBeforeCommitting()
    {
        await using var fixture = new ClaimFixture();
        var operationId = Guid.NewGuid();
        var share = CreateShare();
        using (var repository = new LiteDbShareOperationClaimRepository(fixture.Options))
        {
            (await repository.TryAcquireAsync(operationId,
                                              ShareOperationClaimKind.CreateShare,
                                              share.ShareId,
                                              share.Files.Select(file => file.FileId).ToArray(),
                                              CancellationToken.None)).Should().NotBeNull();
            (await repository.TryBeginCommitAsync(operationId, share, CancellationToken.None)).Should().BeTrue();
        }

        using var reopened = new LiteDbShareOperationClaimRepository(fixture.Options);
        var recovered = (await reopened.GetUnfinishedShareCreationsAsync(share.Files.Select(file => file.FileId).ToArray(),
                                                                         CancellationToken.None)).Should().ContainSingle().Subject;
        recovered.Lifecycle.Should().Be(ShareOperationClaimLifecycle.Committing);
        recovered.ProposedShare.Should().BeEquivalentTo(share);
    }

    private static ShareRecord CreateShare() =>
        new(Guid.NewGuid(),
            $"token-{Guid.NewGuid():N}",
            DateTimeOffset.Parse("2026-08-05T10:00:00Z"),
            DateTimeOffset.Parse("2026-08-06T10:00:00Z"),
            null,
            ShareCleanupState.Pending,
            false,
            null,
            [new(Guid.NewGuid(), "file.bin", null)],
            Guid.NewGuid());

    private sealed class ClaimFixture : IAsyncDisposable
    {
        private readonly String _root = Path.Combine(TestContext.CurrentContext.WorkDirectory,
                                                     "artifacts",
                                                     "claim-repository-tests",
                                                     Guid.NewGuid().ToString("N"));

        public ClaimFixture()
        {
            Directory.CreateDirectory(_root);
            Options = new()
            {
                Metadata = new()
                {
                    LiteDbPath = Path.Combine(_root, "metadata", "shadowdrop.db")
                }
            };
        }

        public ShadowDropOptions Options { get; }

        public ValueTask DisposeAsync()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, true);
            }

            return ValueTask.CompletedTask;
        }
    }
}
