// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Infrastructure.Security;

using FluentAssertions;
using ShadowDrop.Api.Infrastructure.Security;

/// <summary>
/// Provider-independent behavioral contract for <see cref="IUploadCredentialRepository"/>. Executed against the
/// LiteDB implementation as a unit test and against the MongoDB implementation as an integration test, so both
/// providers are held to identical semantics.
/// </summary>
internal static class UploadCredentialRepositoryContract
{
    public static async Task AssertContractAsync(IUploadCredentialRepository repository)
    {
        var now = DateTimeOffset.FromUnixTimeMilliseconds(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        var record = CreateRecord(now);

        (await repository.TryCreateAsync(record, CancellationToken.None)).Should().BeTrue();
        (await repository.TryCreateAsync(record with
        {
            CredentialId = Guid.NewGuid()
        }, CancellationToken.None)).Should().BeFalse("the selector digest must be unique");
        (await repository.TryCreateAsync(record with
        {
            SelectorDigestBase64 = $"digest-{Guid.NewGuid():N}"
        }, CancellationToken.None)).Should().BeFalse("the management id must be unique");

        (await repository.GetAsync(record.CredentialId, CancellationToken.None)).Should().BeEquivalentTo(record);
        (await repository.GetAsync(Guid.NewGuid(), CancellationToken.None)).Should().BeNull();
        (await repository.FindBySelectorDigestAsync(record.SelectorDigestBase64, CancellationToken.None)).Should().BeEquivalentTo(record);
        (await repository.FindBySelectorDigestAsync($"digest-{Guid.NewGuid():N}", CancellationToken.None)).Should().BeNull();

        await AssertUsageRecordingContractAsync(repository, record.CredentialId, now);
        await AssertRevocationContractAsync(repository, record.CredentialId, now);
    }

    public static async Task AssertListPaginationContractAsync(IUploadCredentialRepository repository)
    {
        var baseline = DateTimeOffset.FromUnixTimeMilliseconds(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        var created = new List<UploadCredentialRecord>();
        for (var offset = 0; offset < 5; offset++)
        {
            var record = CreateRecord(baseline.AddSeconds(offset));
            created.Add(record);
            (await repository.TryCreateAsync(record, CancellationToken.None)).Should().BeTrue();
        }

        var expectedNewestFirst = created.OrderByDescending(x => x.CreatedAtUtc).Select(x => x.CredentialId).ToList();

        var listed = new List<Guid>();
        UploadCredentialListCursor? cursor = null;
        do
        {
            var page = await repository.ListNewestFirstAsync(2, cursor, CancellationToken.None);
            page.Credentials.Should().HaveCountLessThanOrEqualTo(2);
            listed.AddRange(page.Credentials.Select(x => x.CredentialId));
            cursor = page.NextCursor;
        } while (cursor is not null);

        // Other contract runs may have inserted credentials into the same shared store, so assert relative
        // order and completeness of this run's records rather than exact page contents.
        listed.Should().OnlyHaveUniqueItems();
        listed.Where(expectedNewestFirst.Contains).Should().Equal(expectedNewestFirst);

        var singlePage = await repository.ListNewestFirstAsync(Int32.MaxValue - 1, null, CancellationToken.None);
        singlePage.NextCursor.Should().BeNull();
        singlePage.Credentials.Select(x => x.CredentialId).Where(expectedNewestFirst.Contains)
                  .Should().Equal(expectedNewestFirst);
    }

    public static UploadCredentialRecord CreateRecord(DateTimeOffset createdAtUtc, String? name = null) =>
        new(Guid.NewGuid(),
            name ?? $"credential-{Guid.NewGuid():N}",
            createdAtUtc,
            null,
            null,
            null,
            1024,
            4096,
            $"digest-{Guid.NewGuid():N}",
            "hash",
            "salt",
            100_000,
            1);

    private static async Task AssertRevocationContractAsync(IUploadCredentialRepository repository, Guid credentialId,
                                                            DateTimeOffset now)
    {
        (await repository.RevokeAsync(Guid.NewGuid(), now, CancellationToken.None)).Should().BeNull();

        var firstRevocation = now.AddMinutes(1);
        var revoked = await repository.RevokeAsync(credentialId, firstRevocation, CancellationToken.None);
        revoked!.RevokedAtUtc.Should().Be(firstRevocation);

        var revokedAgain = await repository.RevokeAsync(credentialId, firstRevocation.AddMinutes(5), CancellationToken.None);
        revokedAgain!.RevokedAtUtc.Should().Be(firstRevocation, "the first revocation timestamp must win");

        await repository.RecordUsageAsync(credentialId, firstRevocation.AddMinutes(10), CancellationToken.None);
        var afterUsage = await repository.GetAsync(credentialId, CancellationToken.None);
        afterUsage!.RevokedAtUtc.Should().Be(firstRevocation, "usage recording must not alter revocation state");
    }

    private static async Task AssertUsageRecordingContractAsync(IUploadCredentialRepository repository, Guid credentialId,
                                                                DateTimeOffset now)
    {
        await repository.RecordUsageAsync(Guid.NewGuid(), now, CancellationToken.None);

        await repository.RecordUsageAsync(credentialId, now, CancellationToken.None);
        (await repository.GetAsync(credentialId, CancellationToken.None))!.LastUsedAtUtc.Should().Be(now);

        await repository.RecordUsageAsync(credentialId, now.AddSeconds(-30), CancellationToken.None);
        (await repository.GetAsync(credentialId, CancellationToken.None))!.LastUsedAtUtc
                                                                          .Should()
                                                                          .Be(now, "an older timestamp must never lower the monotonic last-used value");

        await repository.RecordUsageAsync(credentialId, now.AddSeconds(30), CancellationToken.None);
        (await repository.GetAsync(credentialId, CancellationToken.None))!.LastUsedAtUtc.Should().Be(now.AddSeconds(30));
    }
}
