// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Shares;

using FluentAssertions;
using LiteDB;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Testing;
using NUnit.Framework;
using ShadowDrop.Api.Configuration;
using ShadowDrop.Api.Shares;
using ShadowDrop.Api.Uploads;
using System.Net.Mime;

public sealed class CreateShareServiceTests
{
    [Test]
    public async Task CreateAsync_ShouldLogCreationWithoutTokenValues()
    {
        await using var fixture = new SharePersistenceFixture();
        var options = fixture.CreateOptions();
        using var uploadedFileRepository = new LiteDbUploadedFileMetadataRepository(options, NullLogger<LiteDbUploadedFileMetadataRepository>.Instance);
        using var shareRepository = new LiteDbShareMetadataRepository(options);
        var fileId = await ReserveAndCompleteAsync(uploadedFileRepository, CreateUploadedFileRecord(Guid.NewGuid(), "cipher.bin"));
        var collector = new FakeLogCollector();
        var sut = new CreateShareService(uploadedFileRepository, shareRepository, TimeProvider.System, new FakeLogger<CreateShareService>(collector));
        var request = new CreateShareRequest(DateTimeOffset.Parse("2026-06-01T00:00:00Z"),
                                             [new(fileId, "Display.bin")],
                                             GenerateDownloadBearerToken: true,
                                             DownloadBearerTokenExpiresAtUtc: DateTimeOffset.Parse("2026-05-30T00:00:00Z"));

        var result = await sut.CreateAsync(request, CancellationToken.None);

        var logRecords = collector.GetSnapshot();
        logRecords.Should().Contain(logRecord => logRecord.Level == LogLevel.Information && logRecord.Message.Contains("Share created"));
        var creationRecord = logRecords.Single(logRecord => logRecord.Message.Contains("Share created"));
        var values = creationRecord.StructuredState!.Select(pair => pair.Value).ToList();
        values.Should().NotContain(value => value != null && value.Contains(result.ShareToken));
        values.Should().NotContain(value => value != null && value.Contains(result.DownloadBearerToken!));
        creationRecord.StructuredState!.Should().Contain(pair => pair.Key == "ShareId" && pair.Value == result.ShareId.ToString());
    }

    [Test]
    public async Task CreateAsync_ShouldPersistHashedTokensAndMetadata()
    {
        await using var fixture = new SharePersistenceFixture();
        var options = fixture.CreateOptions();
        using var uploadedFileRepository = new LiteDbUploadedFileMetadataRepository(options, NullLogger<LiteDbUploadedFileMetadataRepository>.Instance);
        using var shareRepository = new LiteDbShareMetadataRepository(options);
        var fileId = await ReserveAndCompleteAsync(uploadedFileRepository, CreateUploadedFileRecord(Guid.NewGuid(), "cipher.bin"));
        var sut = new CreateShareService(uploadedFileRepository, shareRepository, TimeProvider.System, NullLogger<CreateShareService>.Instance);
        var request = new CreateShareRequest(DateTimeOffset.Parse("2026-06-01T00:00:00Z"),
                                             [new(fileId, "Display.bin")],
                                             GenerateDownloadBearerToken: true,
                                             DownloadBearerTokenExpiresAtUtc: DateTimeOffset.Parse("2026-05-30T00:00:00Z"));

        var result = await sut.CreateAsync(request, CancellationToken.None);
        var storedShare = await shareRepository.GetAsync(result.ShareId, CancellationToken.None);

        storedShare.Should().NotBeNull();
        storedShare.ShareTokenHashBase64.Should().NotBe(result.ShareToken);
        storedShare.DownloadBearerToken.Should().NotBeNull();
        storedShare.DownloadBearerToken!.TokenHashBase64.Should().NotBe(result.DownloadBearerToken);
        storedShare.CreatedAtUtc.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
        storedShare.ExpiresAtUtc.Should().Be(request.ExpiresAtUtc);
        storedShare.RevokedAtUtc.Should().BeNull();
        storedShare.CleanupState.Should().Be(ShareCleanupState.Pending);
        storedShare.DirectHttpEnabled.Should().BeFalse();
        storedShare.Files.Should().ContainSingle();
        storedShare.Files[0].FileId.Should().Be(fileId);
        storedShare.Files[0].OriginalFileName.Should().Be("cipher.bin");
        storedShare.Files[0].DisplayName.Should().Be("Display.bin");
        result.ShareToken.Should().NotBeNullOrWhiteSpace();
        result.ShareToken.Length.Should().BeGreaterThanOrEqualTo(43);
        result.DownloadBearerToken.Should().NotBeNullOrWhiteSpace();
        result.DownloadBearerToken!.Length.Should().BeGreaterThanOrEqualTo(43);
    }

    [Test]
    public async Task CreateAsync_ShouldRejectDuplicateFileIds()
    {
        await using var fixture = new SharePersistenceFixture();
        var options = fixture.CreateOptions();
        using var uploadedFileRepository = new LiteDbUploadedFileMetadataRepository(options, NullLogger<LiteDbUploadedFileMetadataRepository>.Instance);
        using var shareRepository = new LiteDbShareMetadataRepository(options);
        var fileId = await ReserveAndCompleteAsync(uploadedFileRepository, CreateUploadedFileRecord(Guid.NewGuid(), "cipher.bin"));
        var sut = new CreateShareService(uploadedFileRepository, shareRepository,
                                         TimeProvider.System, NullLogger<CreateShareService>.Instance);
        var request = new CreateShareRequest(DateTimeOffset.Parse("2026-06-01T00:00:00Z"),
                                             [new(fileId), new(fileId)],
                                             GenerateDownloadBearerToken: false);

        Func<Task> act = async () => await sut.CreateAsync(request, CancellationToken.None);

        await act.Should().ThrowAsync<CreateShareValidationException>();
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task CreateAsync_ShouldRejectFileIdsAlreadyReferencedByExistingShare(Boolean revokeExistingShare)
    {
        await using var fixture = new SharePersistenceFixture();
        var options = fixture.CreateOptions();
        using var uploadedFileRepository = new LiteDbUploadedFileMetadataRepository(options, NullLogger<LiteDbUploadedFileMetadataRepository>.Instance);
        using var shareRepository = new LiteDbShareMetadataRepository(options);
        var fileId = await ReserveAndCompleteAsync(uploadedFileRepository, CreateUploadedFileRecord(Guid.NewGuid(), "cipher.bin"));
        var sut = new CreateShareService(uploadedFileRepository, shareRepository, TimeProvider.System, NullLogger<CreateShareService>.Instance);
        var firstRequest = new CreateShareRequest(DateTimeOffset.Parse("2026-06-01T00:00:00Z"),
                                                  [new(fileId)],
                                                  GenerateDownloadBearerToken: false);
        var firstShare = await sut.CreateAsync(firstRequest, CancellationToken.None);
        if (revokeExistingShare)
        {
            (await shareRepository.TryRevokeAsync(firstShare.ShareId,
                                                  DateTimeOffset.Parse("2026-05-01T00:00:00Z"),
                                                  CancellationToken.None)).Should().BeTrue();
        }

        var secondRequest = new CreateShareRequest(DateTimeOffset.Parse("2026-06-02T00:00:00Z"),
                                                   [new(fileId)],
                                                   GenerateDownloadBearerToken: false);
        Func<Task> act = async () => await sut.CreateAsync(secondRequest, CancellationToken.None);

        await act.Should().ThrowAsync<CreateShareValidationException>();
    }

    [TestCase(true, true, true)]
    [TestCase(false, null, false)]
    [TestCase(false, false, true)]
    [TestCase(false, true, false)]
    public async Task CreateAsync_ShouldRejectInvalidModeOrTokenCombinations(Boolean directHttpEnabled,
                                                                             Boolean? generateDownloadBearerToken,
                                                                             Boolean includeDownloadTokenExpiration)
    {
        await using var fixture = new SharePersistenceFixture();
        var options = fixture.CreateOptions();
        using var uploadedFileRepository = new LiteDbUploadedFileMetadataRepository(options, NullLogger<LiteDbUploadedFileMetadataRepository>.Instance);
        using var shareRepository = new LiteDbShareMetadataRepository(options);
        var fileId = await ReserveAndCompleteAsync(uploadedFileRepository, CreateUploadedFileRecord(Guid.NewGuid(), "cipher.bin"));
        var sut = new CreateShareService(uploadedFileRepository, shareRepository, TimeProvider.System, NullLogger<CreateShareService>.Instance);
        var request = new CreateShareRequest(DateTimeOffset.Parse("2026-06-01T00:00:00Z"),
                                             [new(fileId)],
                                             directHttpEnabled,
                                             generateDownloadBearerToken,
                                             includeDownloadTokenExpiration ? DateTimeOffset.Parse("2026-05-30T00:00:00Z") : null);

        Func<Task> act = async () => await sut.CreateAsync(request, CancellationToken.None);

        await act.Should().ThrowAsync<CreateShareValidationException>();
    }

    [Test]
    public async Task CreateAsync_ShouldRejectMissingFiles()
    {
        await using var fixture = new SharePersistenceFixture();
        var options = fixture.CreateOptions();
        using var uploadedFileRepository = new LiteDbUploadedFileMetadataRepository(options, NullLogger<LiteDbUploadedFileMetadataRepository>.Instance);
        using var shareRepository = new LiteDbShareMetadataRepository(options);
        var sut = new CreateShareService(uploadedFileRepository, shareRepository, TimeProvider.System, NullLogger<CreateShareService>.Instance);
        var request = new CreateShareRequest(DateTimeOffset.Parse("2026-06-01T00:00:00Z"),
                                             [new(Guid.NewGuid())],
                                             GenerateDownloadBearerToken: false);

        Func<Task> act = async () => await sut.CreateAsync(request, CancellationToken.None);

        await act.Should().ThrowAsync<CreateShareValidationException>();
    }

    [Test]
    public async Task CreateAsync_ShouldRollback_WhenMetadataCommitFails()
    {
        await using var fixture = new SharePersistenceFixture();
        var options = fixture.CreateOptions();
        using var uploadedFileRepository = new LiteDbUploadedFileMetadataRepository(options, NullLogger<LiteDbUploadedFileMetadataRepository>.Instance);
        var fileId = await ReserveAndCompleteAsync(uploadedFileRepository, CreateUploadedFileRecord(Guid.NewGuid(), "cipher.bin"));
        var failingShareRepository = new LiteDbShareMetadataRepository(options, () => throw new InvalidOperationException("Simulated transaction failure."));
        var sut = new CreateShareService(uploadedFileRepository, failingShareRepository, TimeProvider.System, NullLogger<CreateShareService>.Instance);
        var request = new CreateShareRequest(DateTimeOffset.Parse("2026-06-01T00:00:00Z"),
                                             [new(fileId)],
                                             GenerateDownloadBearerToken: false);

        Func<Task> act = async () => await sut.CreateAsync(request, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        failingShareRepository.Dispose();
        using var database = new LiteDatabase(options.Metadata.LiteDbPath);
        database.GetCollection("shares").Count().Should().Be(0);
    }

    private static UploadedFileRecord CreateUploadedFileRecord(Guid fileId, String originalFileName) =>
        new(fileId,
            $"metadata/{fileId:N}.blob",
            originalFileName,
            128,
            256,
            MediaTypeNames.Application.Octet,
            "1",
            "AES-256-GCM",
            64,
            2,
            Convert.ToBase64String(Enumerable.Range(0, 32).Select(value => (Byte)value).ToArray()),
            new('a', 64));


    private static async Task<Guid> ReserveAndCompleteAsync(IUploadedFileMetadataRepository repository, UploadedFileRecord record)
    {
        var reservedFileId = await repository.ReserveFileIdAsync(CancellationToken.None);
        (await repository.TryClaimReservationAsync(reservedFileId, CancellationToken.None)).Should().BeTrue();
        var completed = await repository.TryCompleteReservationAsync(record with
        {
            FileId = reservedFileId
        }, CancellationToken.None);
        completed.Should().BeTrue();
        return reservedFileId;
    }

    private sealed class SharePersistenceFixture : IAsyncDisposable
    {
        private readonly String _rootDirectory = Path.Combine(TestContext.CurrentContext.WorkDirectory,
                                                              "artifacts",
                                                              "share-tests",
                                                              Guid.NewGuid().ToString("N"));

        public SharePersistenceFixture()
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
