// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Uploads.Progress;

using FluentAssertions;
using NUnit.Framework;
using ShadowDrop.Cli.Uploads.Progress;
using Spectre.Console.Testing;

[TestFixture]
public sealed class SpectreUploadProgressReporterTests
{
    private static readonly UploadProgressFile File = new("alpha.bin", 1, 1, 32);

    private static SpectreUploadProgressReporter CreateReporter(TestConsole console, TestConsole errorConsole) =>
        new(console, errorConsole, TimeProvider.System);

    [Test]
    public async Task RunFileAsync_ShouldReturnValueAndWriteSuccess_WhenUploadSucceeds()
    {
        using var console = new TestConsole();
        using var errorConsole = new TestConsole();
        var reporter = CreateReporter(console, errorConsole);

        var result = await reporter.RunFileAsync<String>(File, (sink, _) =>
        {
            sink!.Bytes.Report(32);
            return Task.FromResult("share-1");
        }, _ => null, CancellationToken.None);

        result.Value.Should().Be("share-1");
        result.ErrorMessage.Should().BeNull();
        console.Output.Should().Contain("SUCCESS")
               .And.Contain("1/1 alpha.bin")
               .And.Contain("32 B/32 B");
        errorConsole.Output.Should().NotContain("FAILED");
    }

    [Test]
    public async Task RunFileAsync_ShouldClampReportedBytesToTotal_WhenSinkOverreports()
    {
        using var console = new TestConsole();
        using var errorConsole = new TestConsole();
        var reporter = CreateReporter(console, errorConsole);

        var result = await reporter.RunFileAsync<String>(File, (sink, _) =>
        {
            sink!.Bytes.Report(9_999);
            return Task.FromResult("share-1");
        }, _ => null, CancellationToken.None);

        result.Value.Should().Be("share-1");
        console.Output.Should().Contain("32 B/32 B");
    }

    [Test]
    public async Task RunFileAsync_ShouldReturnErrorAndWriteFailed_WhenErrorIsClassified()
    {
        using var console = new TestConsole();
        using var errorConsole = new TestConsole();
        var reporter = CreateReporter(console, errorConsole);

        var result = await reporter.RunFileAsync<String>(File,
                                                         (_, _) => throw new InvalidOperationException("raw failure"),
                                                         _ => "friendly failure",
                                                         CancellationToken.None);

        result.Value.Should().BeNull();
        result.ErrorMessage.Should().Be("friendly failure");
        errorConsole.Output.Should().Contain("FAILED")
                    .And.Contain("1/1 alpha.bin")
                    .And.Contain("friendly failure");
        console.Output.Should().NotContain("SUCCESS");
    }

    [Test]
    public async Task RunFileAsync_ShouldRethrow_WhenErrorIsNotClassified()
    {
        using var console = new TestConsole();
        using var errorConsole = new TestConsole();
        var reporter = CreateReporter(console, errorConsole);

        var act = () => reporter.RunFileAsync<String>(File,
                                                      (_, _) => throw new InvalidOperationException("unclassified"),
                                                      _ => null,
                                                      CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("unclassified");
    }

    [Test]
    public async Task RunFileAsync_ShouldWriteRetryLine_WhenSinkReportsRetry()
    {
        using var console = new TestConsole();
        using var errorConsole = new TestConsole();
        var reporter = CreateReporter(console, errorConsole);

        var result = await reporter.RunFileAsync<String>(File, async (sink, cancellationToken) =>
        {
            await sink!.RetryingAsync(2, cancellationToken);
            sink.Bytes.Report(32);
            return "share-1";
        }, _ => null, CancellationToken.None);

        result.Value.Should().Be("share-1");
        console.Output.Should().Contain("RETRY 1/1 alpha.bin attempt 2")
               .And.Contain("SUCCESS");
    }

    [Test]
    public async Task RunFileAsync_ShouldReportOnlyFinalAttemptBytes_AfterRetry()
    {
        using var console = new TestConsole();
        using var errorConsole = new TestConsole();
        var reporter = CreateReporter(console, errorConsole);

        var result = await reporter.RunFileAsync<String>(File, async (sink, cancellationToken) =>
        {
            sink!.Bytes.Report(24);
            await sink.RetryingAsync(2, cancellationToken);
            sink.Bytes.Report(8);
            return "share-1";
        }, _ => null, CancellationToken.None);

        result.Value.Should().Be("share-1");
        console.Output.Should().Contain("RETRY 1/1 alpha.bin attempt 2")
               .And.Contain("SUCCESS")
               .And.Contain("8 B/32 B")
               .And.NotContain("-");
    }

    [Test]
    public async Task ReportFileFailureAsync_ShouldWriteFailedLineToErrorConsole()
    {
        using var console = new TestConsole();
        using var errorConsole = new TestConsole();
        var reporter = CreateReporter(console, errorConsole);

        await reporter.ReportFileFailureAsync(File, "upload rejected", CancellationToken.None);

        errorConsole.Output.Should().Contain("FAILED")
                    .And.Contain("1/1 alpha.bin")
                    .And.Contain("upload rejected");
    }

    [Test]
    public async Task ReportBatchErrorAsync_ShouldWriteMessageToErrorConsole()
    {
        using var console = new TestConsole();
        using var errorConsole = new TestConsole();
        var reporter = CreateReporter(console, errorConsole);

        await reporter.ReportBatchErrorAsync("no files matched [pattern]", CancellationToken.None);

        errorConsole.Output.Should().Contain("no files matched [pattern]");
    }
}
