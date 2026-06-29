// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Downloads.Progress;

using Spectre.Console;

/// <summary>
/// Emits rich Spectre.Console live progress for interactive terminals, including an active-file spinner, percentage, speed, and ETA columns.
/// </summary>
internal sealed class SpectreDownloadProgressReporter(IAnsiConsole console, TimeProvider timeProvider) : IDownloadProgressReporter
{
    private static void CompleteTask(ProgressTask task, Int64? sizeBytes)
    {
        if (sizeBytes is not null)
        {
            task.Value = task.MaxValue;
        }

        task.StopTask();
    }

    private static ProgressTask CreateTask(ProgressContext context, String description, Int64? sizeBytes)
    {
        var task = context.AddTask(Markup.Escape(FormatTaskDescription(description, sizeBytes)), new ProgressTaskSettings
        {
            // Floor at 1 so a zero-byte file (valid) doesn't produce MaxValue = 0 and a 0/0 percentage in Spectre.
            MaxValue = sizeBytes is > 0 ? sizeBytes.Value : 1
        });
        if (sizeBytes is null)
        {
            task.IsIndeterminate = true;
        }

        return task;
    }

    private static String FormatStats(Int64 bytes, TimeSpan elapsed) =>
        $"{HumanReadableSize.FormatBytes(bytes)} in {HumanReadableSize.FormatDuration(elapsed)}, {HumanReadableSize.FormatSpeed(bytes, elapsed)}";

    private static String FormatTaskDescription(String description, Int64? sizeBytes) =>
        sizeBytes is null
            ? description
            : $"{description} ({HumanReadableSize.FormatBytes(sizeBytes.Value)})";

    private static void UpdateTask(ProgressTask task, Int64 value) =>
        // Advance Value even for unknown-size (indeterminate) tasks: IsIndeterminate keeps the bar non-quantified,
        // while the value deltas let Spectre's TransferSpeedColumn report the real transfer rate.
        task.Value = value;

    private Progress CreateProgress() =>
        console.Progress()
               .AutoClear(false)
               .HideCompleted(false)
               .Columns(new SpinnerColumn(),
                        new TaskDescriptionColumn(),
                        new ProgressBarColumn(),
                        new PercentageColumn(),
                        new TransferSpeedColumn(),
                        new RemainingTimeColumn());

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

        await CreateProgress().StartAsync(async context =>
        {
            var overall = totalBytes is null
                ? null
                : context.AddTask("Overall queue", new ProgressTaskSettings
                {
                    MaxValue = totalBytes.Value
                });
            Int64 completedBytes = 0;

            for (var index = 0; index < items.Count; index++)
            {
                var item = items[index];
                var position = index + 1;
                var task = CreateTask(context, $"{position}/{total} {item.FileName}", item.SizeBytes);
                var fileStart = timeProvider.GetTimestamp();
                var capturedCompleted = completedBytes;
                var progress = new TrackingProgress(value =>
                {
                    UpdateTask(task, value);
                    if (overall is not null)
                    {
                        overall.Value = capturedCompleted + value;
                    }
                });

                try
                {
                    await item.DownloadAsync(progress, cancellationToken);
                    var elapsed = timeProvider.GetElapsedTime(fileStart);
                    var bytes = progress.Value;
                    downloaded++;
                    totalDownloadedBytes += bytes;
                    completedBytes += bytes;
                    CompleteTask(task, item.SizeBytes);
                    if (overall is not null)
                    {
                        overall.Value = completedBytes;
                    }

                    console.MarkupLineInterpolated(
                        $"[green]SUCCESS[/] {position}/{total} {item.FileName} -> {item.OutputPath} ({FormatStats(bytes, elapsed)})");
                }
                catch (Exception exception)
                {
                    var message = classifyError(exception);
                    if (message is null)
                    {
                        throw;
                    }

                    failed++;
                    task.StopTask();
                    console.MarkupLineInterpolated($"[red]FAILED[/] {position}/{total} {item.FileName} -> {item.OutputPath}: {message}");
                }
            }
        });

        var totalElapsed = timeProvider.GetElapsedTime(queueStart);
        console.MarkupLineInterpolated(
            $"SUMMARY downloaded {downloaded}/{total} files, failed {failed} {(failed == 1 ? "file" : "files")} ({FormatStats(totalDownloadedBytes, totalElapsed)})");
        return new(downloaded, failed);
    }

    public async Task RunSingleAsync(String fileName,
                                     Int64? sizeBytes,
                                     Func<IProgress<Int64>?, CancellationToken, Task> downloadAsync,
                                     CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(downloadAsync);

        Int64 bytes = 0;
        var start = timeProvider.GetTimestamp();
        await CreateProgress().StartAsync(async context =>
        {
            var task = CreateTask(context, fileName, sizeBytes);
            var progress = new TrackingProgress(value => UpdateTask(task, value));
            await downloadAsync(progress, cancellationToken);
            bytes = progress.Value;
            CompleteTask(task, sizeBytes);
        });

        var elapsed = timeProvider.GetElapsedTime(start);
        console.MarkupLineInterpolated($"[green]SUCCESS[/] {fileName} ({FormatStats(bytes, elapsed)})");
        console.MarkupLineInterpolated($"SUMMARY downloaded 1 file ({FormatStats(bytes, elapsed)})");
    }
}
