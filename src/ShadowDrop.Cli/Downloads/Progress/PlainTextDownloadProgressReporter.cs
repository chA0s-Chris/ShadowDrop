// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Downloads.Progress;

/// <summary>
/// Emits deterministic plain-text download lifecycle lines to standard error without live progress, suitable for redirected stderr and CI.
/// </summary>
internal sealed class PlainTextDownloadProgressReporter(TextWriter standardError, TimeProvider timeProvider) : IDownloadProgressReporter
{
    private static String FormatStart(Int32? position, Int32? total, String fileName, Int64? sizeBytes)
    {
        var prefix = position is null
            ? $"START {fileName}"
            : $"START {position}/{total} {fileName}";
        return sizeBytes is null
            ? prefix
            : $"{prefix} ({HumanReadableSize.FormatBytes(sizeBytes.Value)})";
    }

    private static String FormatStats(Int64 bytes, TimeSpan elapsed) =>
        $"{HumanReadableSize.FormatBytes(bytes)} in {HumanReadableSize.FormatDuration(elapsed)}, {HumanReadableSize.FormatSpeed(bytes, elapsed)}";

    public async Task<DownloadQueueSummary> RunQueueAsync(IReadOnlyList<QueueDownloadItem> items,
                                                          Int64? totalBytes,
                                                          Func<Exception, String?> classifyError,
                                                          CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(classifyError);

        var total = items.Count;
        var downloaded = 0;
        var failed = 0;
        Int64 totalDownloadedBytes = 0;
        var queueStart = timeProvider.GetTimestamp();

        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            var position = index + 1;
            var fileName = DisplayText.SingleLine(item.FileName);
            var outputPath = DisplayText.SingleLine(item.OutputPath);
            await standardError.WriteLineAsync(FormatStart(position, total, fileName, item.SizeBytes));
            var progress = new TrackingProgress();
            var fileStart = timeProvider.GetTimestamp();
            try
            {
                await item.DownloadAsync(progress, cancellationToken);
                var elapsed = timeProvider.GetElapsedTime(fileStart);
                var bytes = progress.TransferredValue;
                downloaded++;
                totalDownloadedBytes += bytes;
                await standardError.WriteLineAsync(
                    $"SUCCESS {position}/{total} {fileName} -> {outputPath} ({FormatStats(bytes, elapsed)})");
            }
            catch (Exception exception)
            {
                var message = classifyError(exception);
                if (message is null)
                {
                    throw;
                }

                failed++;
                await standardError.WriteLineAsync($"FAILED {position}/{total} {fileName} -> {outputPath}: {message}");
            }
        }

        var totalElapsed = timeProvider.GetElapsedTime(queueStart);
        await standardError.WriteLineAsync(
            $"SUMMARY downloaded {downloaded}/{total} files, failed {failed} {(failed == 1 ? "file" : "files")} ({FormatStats(totalDownloadedBytes, totalElapsed)})");
        return new(downloaded, failed);
    }

    public async Task<Boolean> RunSingleAsync(String fileName,
                                              Int64? sizeBytes,
                                              Func<IProgress<Int64>?, CancellationToken, Task> downloadAsync,
                                              Func<Exception, String?> classifyError,
                                              CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(downloadAsync);
        ArgumentNullException.ThrowIfNull(classifyError);

        fileName = DisplayText.SingleLine(fileName);
        await standardError.WriteLineAsync(FormatStart(null, null, fileName, sizeBytes));
        var progress = new TrackingProgress();
        var start = timeProvider.GetTimestamp();
        try
        {
            await downloadAsync(progress, cancellationToken);
        }
        catch (Exception exception)
        {
            var message = classifyError(exception);
            if (message is null)
            {
                throw;
            }

            var failedElapsed = timeProvider.GetElapsedTime(start);
            await standardError.WriteLineAsync($"FAILED {fileName}: {message}");
            await standardError.WriteLineAsync($"SUMMARY downloaded 0 files, failed 1 file ({FormatStats(progress.TransferredValue, failedElapsed)})");
            return false;
        }

        var elapsed = timeProvider.GetElapsedTime(start);
        var bytes = progress.TransferredValue;
        await standardError.WriteLineAsync($"SUCCESS {fileName} ({FormatStats(bytes, elapsed)})");
        await standardError.WriteLineAsync($"SUMMARY downloaded 1 file ({FormatStats(bytes, elapsed)})");
        return true;
    }
}
