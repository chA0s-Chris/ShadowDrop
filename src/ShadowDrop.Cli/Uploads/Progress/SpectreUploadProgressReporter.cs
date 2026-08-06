// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Uploads.Progress;

using ShadowDrop.Cli.Downloads.Progress;
using Spectre.Console;

/// <summary>
/// Emits rich Spectre.Console upload progress to standard error for interactive terminals.
/// </summary>
internal sealed class SpectreUploadProgressReporter
    : IUploadProgressReporter
{
    private readonly IAnsiConsole _console;
    private readonly IAnsiConsole _errorConsole;
    private readonly TimeProvider _timeProvider;

    public SpectreUploadProgressReporter(IAnsiConsole console,
                                         IAnsiConsole errorConsole,
                                         TimeProvider timeProvider)
    {
        _console = console;
        _errorConsole = errorConsole;
        _timeProvider = timeProvider;
    }

    private static void CompleteTask(ProgressTask task)
    {
        task.Value = task.MaxValue;
        task.StopTask();
    }

    private static String FormatDescription(UploadProgressFile file) =>
        $"{file.Position}/{file.Total} {DisplayText.SingleLine(file.FileName)} ({HumanReadableSize.FormatBytes(file.TotalBytes)})";

    private static String FormatFile(UploadProgressFile file) => $"{file.Position}/{file.Total} {DisplayText.SingleLine(file.FileName)}";

    private static String FormatStats(Int64 bytes, Int64 totalBytes, TimeSpan elapsed) =>
        $"{HumanReadableSize.FormatBytes(bytes)}/{HumanReadableSize.FormatBytes(totalBytes)} in {HumanReadableSize.FormatDuration(elapsed)}, "
        + HumanReadableSize.FormatSpeed(bytes, elapsed);

    private Progress CreateProgress() =>
        _console.Progress()
                .AutoClear(false)
                .HideCompleted(false)
                .Columns(new SpinnerColumn(),
                         new TaskDescriptionColumn(),
                         new ProgressBarColumn(),
                         new PercentageColumn(),
                         new TransferSpeedColumn(),
                         new RemainingTimeColumn());

    public Task ReportBatchErrorAsync(String message, CancellationToken cancellationToken)
    {
        _errorConsole.MarkupLine(Markup.Escape(message));
        return Task.CompletedTask;
    }

    public Task ReportFileFailureAsync(UploadProgressFile file, String message, CancellationToken cancellationToken)
    {
        _errorConsole.MarkupLineInterpolated($"[red]FAILED[/] {FormatFile(file)}: {message}");
        return Task.CompletedTask;
    }

    public async Task<UploadProgressResult<T>> RunFileAsync<T>(UploadProgressFile file,
                                                               Func<UploadProgressSink?, CancellationToken, Task<T>> uploadAsync,
                                                               Func<Exception, String?> classifyError,
                                                               CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(uploadAsync);
        ArgumentNullException.ThrowIfNull(classifyError);

        T? value = default;
        String? failureMessage = null;
        Int64 transferredBytes = 0;
        var attemptStart = _timeProvider.GetTimestamp();

        await CreateProgress().StartAsync(async context =>
        {
            ProgressTask CreateTask()
            {
                return context.AddTask(Markup.Escape(FormatDescription(file)), new()
                {
                    MaxValue = file.TotalBytes > 0 ? file.TotalBytes : 1
                });
            }

            var task = CreateTask();
            var progress = new SynchronousProgress(bytes =>
            {
                transferredBytes = Math.Clamp(bytes, 0, file.TotalBytes);
                task.Value = transferredBytes;
            });
            var sink = new UploadProgressSink(progress, (attempt, _) =>
            {
                task.StopTask();
                task = CreateTask();
                transferredBytes = 0;
                attemptStart = _timeProvider.GetTimestamp();
                _console.MarkupLineInterpolated($"RETRY {FormatFile(file)} attempt {attempt}");
                return Task.CompletedTask;
            });

            try
            {
                value = await uploadAsync(sink, cancellationToken);
                CompleteTask(task);
            }
            catch (Exception exception)
            {
                var message = classifyError(exception);
                if (message is null)
                {
                    throw;
                }

                failureMessage = message;
                task.StopTask();
            }
        });

        var elapsed = _timeProvider.GetElapsedTime(attemptStart);
        if (failureMessage is not null)
        {
            _errorConsole.MarkupLineInterpolated(
                $"[red]FAILED[/] {FormatFile(file)}: {failureMessage} ({FormatStats(transferredBytes, file.TotalBytes, elapsed)})");
            return new(default, failureMessage);
        }

        _console.MarkupLineInterpolated($"[green]SUCCESS[/] {FormatFile(file)} ({FormatStats(transferredBytes, file.TotalBytes, elapsed)})");
        return new(value, null);
    }

    private sealed class SynchronousProgress : IProgress<Int64>
    {
        private readonly Action<Int64> _onReport;

        public SynchronousProgress(Action<Int64> onReport)
        {
            _onReport = onReport;
        }

        public void Report(Int64 value) => _onReport(value);
    }
}
