// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Shares;

using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using NUnit.Framework;
using ShadowDrop.Api.Shares;

public sealed class ShareRevocationServiceTests
{
    [Test]
    public async Task RevokeAsync_ShouldLogInformation_WhenShareIsRevoked()
    {
        var shareId = Guid.NewGuid();
        var repository = new StubShareMetadataRepository(revokeResult: true);
        var collector = new FakeLogCollector();
        var sut = new ShareRevocationService(repository, TimeProvider.System, new FakeLogger<ShareRevocationService>(collector));

        var revoked = await sut.RevokeAsync(shareId, CancellationToken.None);

        revoked.Should().BeTrue();
        var logRecords = collector.GetSnapshot();
        logRecords.Should().ContainSingle();
        logRecords[0].Level.Should().Be(LogLevel.Information);
        logRecords[0].StructuredState!.Should().Contain(pair => pair.Key == "ShareId" && pair.Value == shareId.ToString());
    }

    [Test]
    public async Task RevokeAsync_ShouldLogWarning_WhenShareIsNotFound()
    {
        var shareId = Guid.NewGuid();
        var repository = new StubShareMetadataRepository(revokeResult: false);
        var collector = new FakeLogCollector();
        var sut = new ShareRevocationService(repository, TimeProvider.System, new FakeLogger<ShareRevocationService>(collector));

        var revoked = await sut.RevokeAsync(shareId, CancellationToken.None);

        revoked.Should().BeFalse();
        var logRecords = collector.GetSnapshot();
        logRecords.Should().ContainSingle();
        logRecords[0].Level.Should().Be(LogLevel.Warning);
    }

    private sealed class StubShareMetadataRepository(Boolean revokeResult) : IShareMetadataRepository
    {
        public Task CreateAsync(ShareRecord record, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ShareRecord?> GetAsync(Guid shareId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ShareRecord?> GetByShareTokenHashAsync(String shareTokenHashBase64, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ShareRecord>> GetCleanupCandidatesAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ShareStatusCounts> GetStatusCountsAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Boolean> TryRevokeAsync(Guid shareId, DateTimeOffset revokedAtUtc, CancellationToken cancellationToken) =>
            Task.FromResult(revokeResult);

        public Task<Boolean> TryUpdateCleanupStateAsync(Guid shareId, ShareCleanupState cleanupState, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
