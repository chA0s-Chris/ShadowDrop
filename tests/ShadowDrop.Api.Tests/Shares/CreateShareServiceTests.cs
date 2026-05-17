// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Shares;

using FluentAssertions;
using LiteDB;
using NUnit.Framework;
using ShadowDrop.Api.Configuration;
using ShadowDrop.Api.Shares;
using ShadowDrop.Api.Uploads;
using System.Net.Mime;

public sealed class CreateShareServiceTests
{
    [Test]
    public async Task CreateAsync_ShouldPersistHashedTokensAndMetadata()
    {
        await using var fixture = new SharePersistenceFixture();
        var options = fixture.CreateOptions();
        using var uploadedFileRepository = new LiteDbUploadedFileMetadataRepository(options);
        using var shareRepository = new LiteDbShareMetadataRepository(options);
        var fileId = Guid.NewGuid();
        await uploadedFileRepository.SaveAsync(CreateUploadedFileRecord(fileId, "cipher.bin"), CancellationToken.None);
        var sut = new CreateShareService(uploadedFileRepository, shareRepository, TimeProvider.System);
        var request = new CreateShareRequest(DateTimeOffset.Parse("2026-06-01T00:00:00Z"),
                                             [new CreateShareFileRequest(fileId, "Display.bin")],
                                             GenerateDownloadBearerToken: true,
                                             DownloadBearerTokenExpiresAtUtc: DateTimeOffset.Parse("2026-05-30T00:00:00Z"));

        var result = await sut.CreateAsync(request, CancellationToken.None);
        var storedShare = await shareRepository.GetAsync(result.ShareId, CancellationToken.None);

        storedShare.Should().NotBeNull();
        storedShare!.ShareTokenHashBase64.Should().NotBe(result.ShareToken);
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
        using var uploadedFileRepository = new LiteDbUploadedFileMetadataRepository(options);
        using var shareRepository = new LiteDbShareMetadataRepository(options);
        var fileId = Guid.NewGuid();
        await uploadedFileRepository.SaveAsync(CreateUploadedFileRecord(fileId, "cipher.bin"), CancellationToken.None);
        var sut = new CreateShareService(uploadedFileMetadataRepository: uploadedFileRepository, shareMetadataRepository: shareRepository,
                                         timeProvider: TimeProvider.System);
        var request = new CreateShareRequest(DateTimeOffset.Parse("2026-06-01T00:00:00Z"),
                                             [new CreateShareFileRequest(fileId), new CreateShareFileRequest(fileId)],
                                             GenerateDownloadBearerToken: false);

        Func<Task> act = async () => await sut.CreateAsync(request, CancellationToken.None);

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
        using var uploadedFileRepository = new LiteDbUploadedFileMetadataRepository(options);
        using var shareRepository = new LiteDbShareMetadataRepository(options);
        var fileId = Guid.NewGuid();
        await uploadedFileRepository.SaveAsync(CreateUploadedFileRecord(fileId, "cipher.bin"), CancellationToken.None);
        var sut = new CreateShareService(uploadedFileRepository, shareRepository, TimeProvider.System);
        var request = new CreateShareRequest(DateTimeOffset.Parse("2026-06-01T00:00:00Z"),
                                             [new CreateShareFileRequest(fileId)],
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
        using var uploadedFileRepository = new LiteDbUploadedFileMetadataRepository(options);
        using var shareRepository = new LiteDbShareMetadataRepository(options);
        var sut = new CreateShareService(uploadedFileRepository, shareRepository, TimeProvider.System);
        var request = new CreateShareRequest(DateTimeOffset.Parse("2026-06-01T00:00:00Z"),
                                             [new CreateShareFileRequest(Guid.NewGuid())],
                                             GenerateDownloadBearerToken: false);

        Func<Task> act = async () => await sut.CreateAsync(request, CancellationToken.None);

        await act.Should().ThrowAsync<CreateShareValidationException>();
    }

    [Test]
    public async Task CreateAsync_ShouldRollback_WhenMetadataCommitFails()
    {
        await using var fixture = new SharePersistenceFixture();
        var options = fixture.CreateOptions();
        using var uploadedFileRepository = new LiteDbUploadedFileMetadataRepository(options);
        var fileId = Guid.NewGuid();
        await uploadedFileRepository.SaveAsync(CreateUploadedFileRecord(fileId, "cipher.bin"), CancellationToken.None);
        var failingShareRepository = new LiteDbShareMetadataRepository(options, () => throw new InvalidOperationException("Simulated transaction failure."));
        var sut = new CreateShareService(uploadedFileRepository, failingShareRepository, TimeProvider.System);
        var request = new CreateShareRequest(DateTimeOffset.Parse("2026-06-01T00:00:00Z"),
                                             [new CreateShareFileRequest(fileId)],
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
            new String('a', 64));

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
                Metadata = new MetadataOptions
                {
                    LiteDbPath = Path.Combine(_rootDirectory, "metadata", "shadowdrop.db")
                },
                Storage = new StorageOptions
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
