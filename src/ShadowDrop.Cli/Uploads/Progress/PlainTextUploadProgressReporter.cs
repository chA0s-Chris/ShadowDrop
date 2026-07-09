// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Uploads.Progress;

using ShadowDrop.Cli.Downloads.Progress;
using System.Globalization;

/// <summary>
/// Emits deterministic upload lifecycle and progress lines to standard error for redirected streams and CI.
/// </summary>
internal sealed class PlainTextUploadProgressReporter(TextWriter standardError, TimeProvider timeProvider) : IUploadProgressReporter
{
    private static Double CalculatePercentage(Int64 bytes, Int64 totalBytes)
    {
        if (totalBytes <= 0)
        {
            return 100D;
        }

        var percentage = (Double)bytes / totalBytes * 100D;
        return bytes < totalBytes && Math.Round(percentage, 1) >= 100D
            ? 99.9D
            : percentage;
    }

    private static String FormatFile(UploadProgressFile file) => $"{file.Position}/{file.Total} {DisplayText.SingleLine(file.FileName)}";

    private static String FormatProgress(Int64 bytes, Int64 totalBytes, TimeSpan elapsed)
    {
        bytes = Math.Clamp(bytes, 0, totalBytes);
        var percentage = CalculatePercentage(bytes, totalBytes);
        var text =
            $"{HumanReadableSize.FormatBytes(bytes)}/{HumanReadableSize.FormatBytes(totalBytes)} ({percentage.ToString("0.0", CultureInfo.InvariantCulture)}%)";
        return elapsed >= TimeSpan.FromSeconds(1)
            ? $"{text}, {HumanReadableSize.FormatSpeed(bytes, elapsed)}"
            : text;
    }

    private static String FormatStats(Int64 bytes, Int64 totalBytes, TimeSpan elapsed) =>
        $"{FormatProgress(bytes, totalBytes, elapsed)} in {HumanReadableSize.FormatDuration(elapsed)}";

    public async Task ReportBatchErrorAsync(String message, CancellationToken cancellationToken)
    {
        await standardError.WriteLineAsync(message);
    }

    public async Task ReportFileFailureAsync(UploadProgressFile file, String message, CancellationToken cancellationToken)
    {
        await standardError.WriteLineAsync($"FAILED {FormatFile(file)}: {message}");
    }

    public async Task<UploadProgressResult<T>> RunFileAsync<T>(UploadProgressFile file,
                                                               Func<UploadProgressSink?, CancellationToken, Task<T>> uploadAsync,
                                                               Func<Exception, String?> classifyError,
                                                               CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(uploadAsync);
        ArgumentNullException.ThrowIfNull(classifyError);

        await standardError.WriteLineAsync($"START {FormatFile(file)} ({HumanReadableSize.FormatBytes(file.TotalBytes)})");

        var start = timeProvider.GetTimestamp();
        Int64 transferredBytes = 0;
        var gate = new Object();
        // Track the latest cumulative byte count silently; the plain reporter stays sparse (START/SUCCESS/FAILED/RETRY
        // lifecycle lines only) to match the download plain reporter and avoid one line per chunk on large uploads.
        var progress = new SynchronousProgress(value =>
        {
            lock (gate)
            {
                transferredBytes = Math.Clamp(value, 0, file.TotalBytes);
            }
        });
        var sink = new UploadProgressSink(progress, (attempt, _) =>
        {
            lock (gate)
            {
                transferredBytes = 0;
                standardError.WriteLine($"RETRY {FormatFile(file)} attempt {attempt}");
            }

            return Task.CompletedTask;
        });

        try
        {
            var value = await uploadAsync(sink, cancellationToken);
            var elapsed = timeProvider.GetElapsedTime(start);
            await standardError.WriteLineAsync($"SUCCESS {FormatFile(file)} ({FormatStats(transferredBytes, file.TotalBytes, elapsed)})");
            return new(value, null);
        }
        catch (Exception exception)
        {
            var message = classifyError(exception);
            if (message is null)
            {
                throw;
            }

            var elapsed = timeProvider.GetElapsedTime(start);
            await standardError.WriteLineAsync($"FAILED {FormatFile(file)}: {message} ({FormatStats(transferredBytes, file.TotalBytes, elapsed)})");
            return new(default, message);
        }
    }

    private sealed class SynchronousProgress(Action<Int64> onReport) : IProgress<Int64>
    {
        public void Report(Int64 value) => onReport(value);
    }
}
