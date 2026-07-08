// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Uploads;

using FluentAssertions;
using LiteDB;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using NUnit.Framework;
using ShadowDrop.Api.Configuration;
using ShadowDrop.Api.Uploads;
using ShadowDrop.Contracts;
using System.Net.Mime;

public sealed class UploadedFileMetadataRepositoryStatsTests
{
    [Test]
    public async Task GetActivePendingReservationCountAsync_ShouldCountOnlyUnclaimedUnexpiredReservations()
    {
        await using var fixture = new StatsFixture();
        var options = fixture.CreateOptions();
        using var repository = new LiteDbUploadedFileMetadataRepository(options, new FakeLogger<LiteDbUploadedFileMetadataRepository>(new FakeLogCollector()));

        var pendingReservation = await repository.ReserveFileIdAsync(CancellationToken.None);
        var claimedReservation = await repository.ReserveFileIdAsync(CancellationToken.None);
        (await repository.TryClaimReservationAsync(claimedReservation, CancellationToken.None)).Should().BeTrue();
        var expiredReservation = await repository.ReserveFileIdAsync(CancellationToken.None);
        ExpireReservation(options.Metadata.LiteDbPath, expiredReservation);

        var count = await repository.GetActivePendingReservationCountAsync(CancellationToken.None);

        count.Should().Be(1);
        pendingReservation.Should().NotBeEmpty();
    }

    [Test]
    public async Task GetStorageStatsAsync_ShouldCountOnlyCompletedFiles_AndSumEncryptedLength()
    {
        await using var fixture = new StatsFixture();
        var options = fixture.CreateOptions();
        using var repository = new LiteDbUploadedFileMetadataRepository(options, new FakeLogger<LiteDbUploadedFileMetadataRepository>(new FakeLogCollector()));

        await CompleteUploadAsync(repository, 100);
        await CompleteUploadAsync(repository, 250);
        _ = await repository.ReserveFileIdAsync(CancellationToken.None);

        var stats = await repository.GetStorageStatsAsync(CancellationToken.None);

        stats.CompletedFileCount.Should().Be(2);
        stats.TotalEncryptedBytes.Should().Be(350);
    }

    [Test]
    public async Task ReservationLifecycle_ShouldLogLifecycleEvents_WithOnlyFileIdAndBlobKey()
    {
        await using var fixture = new StatsFixture();
        var options = fixture.CreateOptions();
        var collector = new FakeLogCollector();
        using var repository = new LiteDbUploadedFileMetadataRepository(options, new FakeLogger<LiteDbUploadedFileMetadataRepository>(collector));

        var fileId = await repository.ReserveFileIdAsync(CancellationToken.None);
        (await repository.TryClaimReservationAsync(fileId, CancellationToken.None)).Should().BeTrue();
        const String blobKey = "ab/abcdef.blob";
        var uploadedFileRecord = new UploadedFileRecord(fileId,
                                                        blobKey,
                                                        "cipher.bin",
                                                        100,
                                                        100,
                                                        MediaTypeNames.Application.Octet,
                                                        FormatConstants.EncryptionFormatVersion,
                                                        FormatConstants.Aes256GcmAlgorithmId,
                                                        64,
                                                        1,
                                                        Convert.ToBase64String(Enumerable.Range(0, 32).Select(value => (Byte)value).ToArray()),
                                                        new('a', 64));
        (await repository.TryCompleteReservationAsync(uploadedFileRecord, CancellationToken.None)).Should().BeTrue();

        var logRecords = collector.GetSnapshot();

        logRecords.Should().NotBeEmpty();
        logRecords.Should().OnlyContain(logRecord => logRecord.Level == LogLevel.Information || logRecord.Level == LogLevel.Debug);
        logRecords.Should().Contain(logRecord => logRecord.Level == LogLevel.Information && logRecord.Message.Contains("created"));
        logRecords.Should().Contain(logRecord => logRecord.Level == LogLevel.Information && logRecord.Message.Contains("claimed"));

        // UploadPersistenceService owns the operator-facing "Upload completed" event; the repository traces it at debug.
        logRecords.Should().Contain(logRecord => logRecord.Level == LogLevel.Debug && logRecord.Message.Contains("completed"));

        foreach (var logRecord in logRecords)
        {
            var values = logRecord.StructuredState!.Select(pair => pair.Value);
            values.Should().NotContain(value => value != null && value.Contains(uploadedFileRecord.KdfSaltBase64));
            values.Should().NotContain(value => value != null && value.Contains(uploadedFileRecord.PlaintextSha256!));
        }
    }

    private static async Task CompleteUploadAsync(IUploadedFileMetadataRepository repository, Int64 encryptedLength)
    {
        var fileId = await repository.ReserveFileIdAsync(CancellationToken.None);
        (await repository.TryClaimReservationAsync(fileId, CancellationToken.None)).Should().BeTrue();
        var record = new UploadedFileRecord(fileId,
                                            $"blob/{fileId:N}.blob",
                                            "cipher.bin",
                                            encryptedLength,
                                            encryptedLength,
                                            MediaTypeNames.Application.Octet,
                                            FormatConstants.EncryptionFormatVersion,
                                            FormatConstants.Aes256GcmAlgorithmId,
                                            64,
                                            1,
                                            Convert.ToBase64String(Enumerable.Range(0, 32).Select(value => (Byte)value).ToArray()),
                                            new('a', 64));
        (await repository.TryCompleteReservationAsync(record, CancellationToken.None)).Should().BeTrue();
    }

    private static void ExpireReservation(String databasePath, Guid fileId)
    {
        using var database = new LiteDatabase(databasePath);
        var collection = database.GetCollection("uploaded_files");
        var document = collection.FindById(fileId);
        ((Object?)document).Should().NotBeNull();
        document!["ReservedAtUnixTimeMilliseconds"] = DateTimeOffset.UtcNow.AddDays(-2).ToUnixTimeMilliseconds();
        collection.Update(document);
    }

    private sealed class StatsFixture : IAsyncDisposable
    {
        private readonly String _rootDirectory = Path.Combine(TestContext.CurrentContext.WorkDirectory,
                                                              "artifacts",
                                                              "upload-stats-tests",
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
