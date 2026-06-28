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
            await standardError.WriteLineAsync(FormatStart(position, total, item.FileName, item.SizeBytes));
            var progress = new TrackingProgress();
            var fileStart = timeProvider.GetTimestamp();
            try
            {
                await item.DownloadAsync(progress, cancellationToken);
                var elapsed = timeProvider.GetElapsedTime(fileStart);
                var bytes = progress.Value;
                downloaded++;
                totalDownloadedBytes += bytes;
                await standardError.WriteLineAsync(
                    $"SUCCESS {position}/{total} {item.FileName} -> {item.OutputPath} ({FormatStats(bytes, elapsed)})");
            }
            catch (Exception exception)
            {
                var message = classifyError(exception);
                if (message is null)
                {
                    throw;
                }

                failed++;
                await standardError.WriteLineAsync($"FAILED {position}/{total} {item.FileName} -> {item.OutputPath}: {message}");
            }
        }

        var totalElapsed = timeProvider.GetElapsedTime(queueStart);
        await standardError.WriteLineAsync($"SUMMARY downloaded {downloaded}/{total} files ({FormatStats(totalDownloadedBytes, totalElapsed)})");
        return new(downloaded, failed);
    }

    public async Task RunSingleAsync(String fileName,
                                     Int64? sizeBytes,
                                     Func<IProgress<Int64>?, CancellationToken, Task> downloadAsync,
                                     CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(downloadAsync);

        await standardError.WriteLineAsync(FormatStart(null, null, fileName, sizeBytes));
        var progress = new TrackingProgress();
        var start = timeProvider.GetTimestamp();
        await downloadAsync(progress, cancellationToken);
        var elapsed = timeProvider.GetElapsedTime(start);
        var bytes = progress.Value;
        await standardError.WriteLineAsync($"SUCCESS {fileName} ({FormatStats(bytes, elapsed)})");
        await standardError.WriteLineAsync($"SUMMARY downloaded 1 file ({FormatStats(bytes, elapsed)})");
    }
}
