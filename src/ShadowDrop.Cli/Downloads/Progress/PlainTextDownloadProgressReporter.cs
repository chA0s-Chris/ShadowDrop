// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Downloads.Progress;

/// <summary>
/// Emits deterministic plain-text download lifecycle lines without live progress, suitable for redirected output and CI.
/// Lifecycle and summary lines go to standard output; per-item failures go to standard error.
/// </summary>
internal sealed class PlainTextDownloadProgressReporter
    : IDownloadProgressReporter
{
    private readonly TextWriter _standardError;
    private readonly TextWriter _standardOut;
    private readonly TimeProvider _timeProvider;

    public PlainTextDownloadProgressReporter(TextWriter standardOut,
                                             TextWriter standardError,
                                             TimeProvider timeProvider)
    {
        _standardOut = standardOut;
        _standardError = standardError;
        _timeProvider = timeProvider;
    }

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
        var queueStart = _timeProvider.GetTimestamp();

        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            var position = index + 1;
            var fileName = DisplayText.SingleLine(item.FileName);
            var outputPath = DisplayText.SingleLine(item.OutputPath);
            await _standardOut.WriteLineAsync(FormatStart(position, total, fileName, item.SizeBytes));
            var progress = new TrackingProgress();
            var fileStart = _timeProvider.GetTimestamp();
            try
            {
                await item.DownloadAsync(progress, cancellationToken);
                var elapsed = _timeProvider.GetElapsedTime(fileStart);
                var bytes = progress.TransferredValue;
                downloaded++;
                totalDownloadedBytes += bytes;
                await _standardOut.WriteLineAsync(
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
                await _standardError.WriteLineAsync($"FAILED {position}/{total} {fileName} -> {outputPath}: {message}");
            }
        }

        var totalElapsed = _timeProvider.GetElapsedTime(queueStart);
        await _standardOut.WriteLineAsync(
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
        await _standardOut.WriteLineAsync(FormatStart(null, null, fileName, sizeBytes));
        var progress = new TrackingProgress();
        var start = _timeProvider.GetTimestamp();
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

            var failedElapsed = _timeProvider.GetElapsedTime(start);
            await _standardError.WriteLineAsync($"FAILED {fileName}: {message}");
            await _standardOut.WriteLineAsync($"SUMMARY downloaded 0 files, failed 1 file ({FormatStats(progress.TransferredValue, failedElapsed)})");
            return false;
        }

        var elapsed = _timeProvider.GetElapsedTime(start);
        var bytes = progress.TransferredValue;
        await _standardOut.WriteLineAsync($"SUCCESS {fileName} ({FormatStats(bytes, elapsed)})");
        await _standardOut.WriteLineAsync($"SUMMARY downloaded 1 file ({FormatStats(bytes, elapsed)})");
        return true;
    }
}
