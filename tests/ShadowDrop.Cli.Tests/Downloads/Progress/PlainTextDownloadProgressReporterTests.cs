// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Downloads.Progress;

using FluentAssertions;
using NUnit.Framework;
using ShadowDrop.Cli.Downloads;
using ShadowDrop.Cli.Downloads.Progress;

public sealed class PlainTextDownloadProgressReporterTests
{
    [Test]
    public async Task RunQueueAsync_ShouldEmitIndexedLifecycleLinesAndSummary()
    {
        var standardOut = new StringWriter();
        var standardError = new StringWriter();
        var timeProvider = new ManualTimeProvider();
        var reporter = new PlainTextDownloadProgressReporter(standardOut, standardError, timeProvider);

        QueueDownloadItem[] items =
        [
            new("alpha.bin", 1000, "out/alpha.bin", (progress, _) =>
            {
                // A real download always reports its starting offset (0 here) before any bytes transfer.
                progress!.Report(0);
                timeProvider.Advance(TimeSpan.FromSeconds(1));
                progress.Report(1000);
                return Task.CompletedTask;
            }),
            new("beta.bin", 2000, "out/beta.bin", (_, _) => throw new DownloadCommandException("boom"))
        ];

        var summary = await reporter.RunQueueAsync(items, 3000, ClassifyError, CancellationToken.None);

        summary.Downloaded.Should().Be(1);
        summary.Failed.Should().Be(1);
        var outLines = ReadLines(standardOut);
        outLines.Should().Contain("START 1/2 alpha.bin (1.0 KB)");
        outLines.Should().Contain("SUCCESS 1/2 alpha.bin -> out/alpha.bin (1.0 KB in 1.0s, 1.0 KB/s)");
        outLines.Should().Contain("START 2/2 beta.bin (2.0 KB)");
        outLines.Should().Contain(line => line.StartsWith("SUMMARY downloaded 1/2 files, failed 1 file (1.0 KB in"));

        // Per-item failures stay on stderr so a redirected stdout carries only the lifecycle record.
        outLines.Should().NotContain(line => line.StartsWith("FAILED"));
        ReadLines(standardError).Should().Equal("FAILED 2/2 beta.bin -> out/beta.bin: boom");
    }

    [Test]
    public async Task RunQueueAsync_ShouldRethrow_WhenErrorIsNotClassified()
    {
        var reporter = new PlainTextDownloadProgressReporter(new StringWriter(), new StringWriter(), new ManualTimeProvider());
        QueueDownloadItem[] items =
        [
            new("alpha.bin", 1000, "out/alpha.bin", (_, _) => throw new InvalidOperationException("unexpected"))
        ];

        var act = async () => await reporter.RunQueueAsync(items, 1000, ClassifyError, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Test]
    public async Task RunSingleAsync_ShouldEmitFailedOnStandardErrorAndSummaryOnStandardOut_WhenErrorIsClassified()
    {
        var standardOut = new StringWriter();
        var standardError = new StringWriter();
        var reporter = new PlainTextDownloadProgressReporter(standardOut, standardError, new ManualTimeProvider());

        var succeeded = await reporter.RunSingleAsync("alpha.bin", 2000,
                                                      (_, _) => throw new DownloadCommandException("boom"),
                                                      ClassifyError,
                                                      CancellationToken.None);

        succeeded.Should().BeFalse();
        var outLines = ReadLines(standardOut);
        outLines[0].Should().Be("START alpha.bin (2.0 KB)");
        outLines.Should().Contain(line => line.StartsWith("SUMMARY downloaded 0 files, failed 1 file ("));
        ReadLines(standardError).Should().Equal("FAILED alpha.bin: boom");
    }

    [Test]
    public async Task RunSingleAsync_ShouldEmitStartSuccessAndSummaryLinesOnStandardOut()
    {
        var standardOut = new StringWriter();
        var standardError = new StringWriter();
        var timeProvider = new ManualTimeProvider();
        var reporter = new PlainTextDownloadProgressReporter(standardOut, standardError, timeProvider);

        await reporter.RunSingleAsync("alpha.bin", 2000, (progress, _) =>
        {
            // A real download always reports its starting offset (0 here) before any bytes transfer.
            progress!.Report(0);
            timeProvider.Advance(TimeSpan.FromSeconds(2));
            progress.Report(2000);
            return Task.CompletedTask;
        }, ClassifyError, CancellationToken.None);

        var lines = ReadLines(standardOut);
        lines.Should().HaveCount(3);
        lines[0].Should().Be("START alpha.bin (2.0 KB)");
        lines[1].Should().Be("SUCCESS alpha.bin (2.0 KB in 2.0s, 1.0 KB/s)");
        lines[2].Should().Be("SUMMARY downloaded 1 file (2.0 KB in 2.0s, 1.0 KB/s)");
        standardError.ToString().Should().BeEmpty();
    }

    [Test]
    public async Task RunSingleAsync_ShouldKeepLifecycleRecordOnOneLine_WhenFileNameContainsLineBreaks()
    {
        var standardOut = new StringWriter();
        var timeProvider = new ManualTimeProvider();
        var reporter = new PlainTextDownloadProgressReporter(standardOut, new StringWriter(), timeProvider);

        await reporter.RunSingleAsync("alpha\r\nINJECTED.bin", 2000, (progress, _) =>
        {
            timeProvider.Advance(TimeSpan.FromSeconds(2));
            progress!.Report(2000);
            return Task.CompletedTask;
        }, ClassifyError, CancellationToken.None);

        // The CR/LF in the name must not split the deterministic records: exactly START/SUCCESS/SUMMARY, with the name collapsed to one line.
        var lines = ReadLines(standardOut);
        lines.Should().HaveCount(3);
        lines[0].Should().Be("START alpha  INJECTED.bin (2.0 KB)");
        lines[1].Should().StartWith("SUCCESS alpha  INJECTED.bin (");
    }

    [Test]
    public async Task RunSingleAsync_ShouldOmitSizeFromStart_WhenSizeUnknown()
    {
        var standardOut = new StringWriter();
        var reporter = new PlainTextDownloadProgressReporter(standardOut, new StringWriter(), new ManualTimeProvider());

        await reporter.RunSingleAsync("alpha.bin", null, (progress, _) =>
        {
            progress!.Report(10);
            return Task.CompletedTask;
        }, ClassifyError, CancellationToken.None);

        ReadLines(standardOut)[0].Should().Be("START alpha.bin");
    }

    [Test]
    public async Task RunSingleAsync_ShouldRethrow_WhenErrorIsNotClassified()
    {
        var reporter = new PlainTextDownloadProgressReporter(new StringWriter(), new StringWriter(), new ManualTimeProvider());

        var act = async () => await reporter.RunSingleAsync("alpha.bin", 2000,
                                                            (_, _) => throw new InvalidOperationException("unexpected"),
                                                            ClassifyError,
                                                            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    private static String? ClassifyError(Exception exception) =>
        exception is DownloadCommandException ? exception.Message : null;

    private static IReadOnlyList<String> ReadLines(StringWriter writer) =>
        writer.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

    private sealed class ManualTimeProvider : TimeProvider
    {
        private Int64 _timestamp;

        public void Advance(TimeSpan delta) => _timestamp += (Int64)(delta.TotalSeconds * TimestampFrequency);

        public override Int64 GetTimestamp() => _timestamp;
    }
}
