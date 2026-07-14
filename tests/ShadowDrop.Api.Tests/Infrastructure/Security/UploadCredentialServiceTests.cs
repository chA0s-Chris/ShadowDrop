// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Infrastructure.Security;

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using ShadowDrop.Api.Infrastructure.Security;

public sealed class UploadCredentialServiceTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-07-14T12:00:00Z");

    [Test]
    public async Task AuthenticateAsync_ShouldRejectExpiredCredential_AndSkipUsageRecording()
    {
        var (service, repository, timeProvider) = CreateService();
        var created = await service.CreateAsync(new("expiring", Now.AddMinutes(5), null, null), CancellationToken.None);
        timeProvider.UtcNow = Now.AddMinutes(6);

        var context = await service.AuthenticateAsync(created.Token, CancellationToken.None);

        context.Should().BeNull();
        repository.UsageRecordings.Should().BeEmpty();
    }

    [Test]
    public async Task AuthenticateAsync_ShouldRejectMalformedToken_WithoutQueryingTheRepository()
    {
        var (service, repository, _) = CreateService();

        var context = await service.AuthenticateAsync("Bearer nonsense", CancellationToken.None);

        context.Should().BeNull();
        repository.SelectorLookups.Should().Be(0);
    }

    [Test]
    public async Task AuthenticateAsync_ShouldRejectRevokedCredential_AndSkipUsageRecording()
    {
        var (service, repository, _) = CreateService();
        var created = await service.CreateAsync(new("revoked", null, null, null), CancellationToken.None);
        await repository.RevokeAsync(created.Credential.CredentialId, Now, CancellationToken.None);

        var context = await service.AuthenticateAsync(created.Token, CancellationToken.None);

        context.Should().BeNull();
        repository.UsageRecordings.Should().BeEmpty();
    }

    [Test]
    public async Task AuthenticateAsync_ShouldRejectUnknownSelector()
    {
        var (service, _, _) = CreateService();

        var context = await service.AuthenticateAsync(UploadCredentialToken.Create().Token, CancellationToken.None);

        context.Should().BeNull();
    }

    [Test]
    public async Task AuthenticateAsync_ShouldRejectWrongSecret_AndSkipUsageRecording()
    {
        var (service, repository, _) = CreateService();
        var created = await service.CreateAsync(new("victim", null, null, null), CancellationToken.None);
        UploadCredentialToken.TryParse(created.Token, out var selector, out _).Should().BeTrue();
        var forged = $"{UploadCredentialToken.Prefix}.{selector}.{UploadCredentialToken.Create().Secret}";

        var context = await service.AuthenticateAsync(forged, CancellationToken.None);

        context.Should().BeNull();
        repository.UsageRecordings.Should().BeEmpty();
    }

    [Test]
    public async Task AuthenticateAsync_ShouldReturnContextAndRecordUsage_ForValidToken()
    {
        var (service, repository, timeProvider) = CreateService();
        var created = await service.CreateAsync(new("automation", Now.AddDays(30), 1024, 4096), CancellationToken.None);
        timeProvider.UtcNow = Now.AddMinutes(1);

        var context = await service.AuthenticateAsync(created.Token, CancellationToken.None);

        context.Should().Be(new UploadCredentialAuthorizationContext(created.Credential.CredentialId, 1024, 4096));
        repository.UsageRecordings.Should().Equal((created.Credential.CredentialId, Now.AddMinutes(1)));
    }

    [Test]
    public async Task CreateAsync_ShouldPersistOnlyDerivedTokenMaterial()
    {
        var (service, repository, _) = CreateService();

        var created = await service.CreateAsync(new("  automation  ", null, null, null), CancellationToken.None);

        UploadCredentialToken.TryParse(created.Token, out var selector, out var secret).Should().BeTrue();
        var record = repository.Records.Should().ContainSingle().Subject;
        record.Should().BeSameAs(created.Credential);
        record.Name.Should().Be("automation");
        record.CreatedAtUtc.Should().Be(Now);
        record.ExpiresAtUtc.Should().BeNull();
        record.RevokedAtUtc.Should().BeNull();
        record.LastUsedAtUtc.Should().BeNull();
        record.SecretHashIterations.Should().BeGreaterThan(0);
        record.SecretHashVersion.Should().Be(1);
        record.SelectorDigestBase64.Should().NotBeNullOrWhiteSpace();
        var persistedValues = new[]
        {
            record.SelectorDigestBase64,
            record.SecretHashBase64,
            record.SecretSaltBase64
        };
        persistedValues.Should().NotContain(selector);
        persistedValues.Should().NotContain(secret);
        persistedValues.Should().NotContain(created.Token);
    }

    [Test]
    public async Task CreateAsync_ShouldRegenerate_WhenSelectorDigestCollides()
    {
        var (service, repository, _) = CreateService();
        repository.RejectNextCreates = 2;

        var created = await service.CreateAsync(new("retried", null, null, null), CancellationToken.None);

        repository.CreateAttempts.Should().Be(3);
        repository.Records.Should().ContainSingle().Which.CredentialId.Should().Be(created.Credential.CredentialId);
    }

    [TestCase("line one\nline two")]
    [TestCase("escape\u001bsequence")]
    public async Task CreateAsync_ShouldRejectControlCharactersInName(String name)
    {
        var (service, repository, _) = CreateService();

        var create = async () => await service.CreateAsync(new(name, null, null, null), CancellationToken.None);

        await create.Should().ThrowAsync<UploadCredentialValidationException>();
        repository.Records.Should().BeEmpty();
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public async Task CreateAsync_ShouldRejectMissingName(String? name)
    {
        var (service, _, _) = CreateService();

        var create = async () => await service.CreateAsync(new(name, null, null, null), CancellationToken.None);

        await create.Should().ThrowAsync<UploadCredentialValidationException>();
    }

    [Test]
    public async Task CreateAsync_ShouldRejectNonPositiveConstraints()
    {
        var (service, _, _) = CreateService();

        var createWithFileLimit = async () => await service.CreateAsync(new("name", null, 0, null), CancellationToken.None);
        var createWithShareLimit = async () => await service.CreateAsync(new("name", null, null, -1), CancellationToken.None);

        await createWithFileLimit.Should().ThrowAsync<UploadCredentialValidationException>();
        await createWithShareLimit.Should().ThrowAsync<UploadCredentialValidationException>();
    }

    [Test]
    public async Task CreateAsync_ShouldRejectPastExpiration()
    {
        var (service, _, _) = CreateService();

        var create = async () => await service.CreateAsync(new("name", Now.AddMinutes(-1), null, null), CancellationToken.None);

        await create.Should().ThrowAsync<UploadCredentialValidationException>();
    }

    [Test]
    public async Task CreateAsync_ShouldRejectTooLongName_AndAcceptMaximumLength()
    {
        var (service, repository, _) = CreateService();
        var maximumLengthName = new String('n', UploadCredentialService.MaxNameLength);

        var createTooLong = async () =>
            await service.CreateAsync(new(maximumLengthName + "n", null, null, null), CancellationToken.None);

        await createTooLong.Should().ThrowAsync<UploadCredentialValidationException>();
        _ = await service.CreateAsync(new(maximumLengthName, null, null, null), CancellationToken.None);
        repository.Records.Should().ContainSingle().Which.Name.Should().Be(maximumLengthName);
    }

    private static (UploadCredentialService Service, InMemoryUploadCredentialRepository Repository, MutableTimeProvider TimeProvider)
        CreateService()
    {
        var repository = new InMemoryUploadCredentialRepository();
        var timeProvider = new MutableTimeProvider
        {
            UtcNow = Now
        };
        return (new(repository, timeProvider, NullLogger<UploadCredentialService>.Instance), repository, timeProvider);
    }

    private sealed class InMemoryUploadCredentialRepository : IUploadCredentialRepository
    {
        private readonly List<UploadCredentialRecord> _records = [];

        public Int32 CreateAttempts { get; private set; }

        public IReadOnlyList<UploadCredentialRecord> Records => _records;

        public Int32 RejectNextCreates { get; set; }

        public Int32 SelectorLookups { get; private set; }

        public List<(Guid CredentialId, DateTimeOffset LastUsedAtUtc)> UsageRecordings { get; } = [];

        public Task<UploadCredentialRecord?> FindBySelectorDigestAsync(String selectorDigestBase64, CancellationToken cancellationToken)
        {
            SelectorLookups++;
            return Task.FromResult(_records.FirstOrDefault(x => x.SelectorDigestBase64 == selectorDigestBase64));
        }

        public Task<UploadCredentialRecord?> GetAsync(Guid credentialId, CancellationToken cancellationToken) =>
            Task.FromResult(_records.FirstOrDefault(x => x.CredentialId == credentialId));

        public Task<UploadCredentialPage> ListNewestFirstAsync(Int32 pageSize, UploadCredentialListCursor? cursor,
                                                               CancellationToken cancellationToken) =>
            throw new NotSupportedException("Listing is not part of these scenarios.");

        public Task RecordUsageAsync(Guid credentialId, DateTimeOffset lastUsedAtUtc, CancellationToken cancellationToken)
        {
            UsageRecordings.Add((credentialId, lastUsedAtUtc));
            return Task.CompletedTask;
        }

        public Task<UploadCredentialRecord?> RevokeAsync(Guid credentialId, DateTimeOffset revokedAtUtc, CancellationToken cancellationToken)
        {
            var record = _records.FirstOrDefault(x => x.CredentialId == credentialId);
            if (record is null)
            {
                return Task.FromResult<UploadCredentialRecord?>(null);
            }

            var revoked = record.RevokedAtUtc is null
                ? record with
                {
                    RevokedAtUtc = revokedAtUtc
                }
                : record;
            _records[_records.IndexOf(record)] = revoked;
            return Task.FromResult<UploadCredentialRecord?>(revoked);
        }

        public Task<Boolean> TryCreateAsync(UploadCredentialRecord record, CancellationToken cancellationToken)
        {
            CreateAttempts++;
            if (RejectNextCreates > 0)
            {
                RejectNextCreates--;
                return Task.FromResult(false);
            }

            _records.Add(record);
            return Task.FromResult(true);
        }
    }

    private sealed class MutableTimeProvider : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; }

        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}
