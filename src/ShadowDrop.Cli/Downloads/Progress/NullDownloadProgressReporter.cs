// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Downloads.Progress;

/// <summary>
/// A reporter that performs downloads without emitting any progress output, used by callers that own their own UX.
/// </summary>
internal sealed class NullDownloadProgressReporter : IDownloadProgressReporter
{
    public static NullDownloadProgressReporter Instance { get; } = new();

    public async Task<DownloadQueueSummary> RunQueueAsync(IReadOnlyList<QueueDownloadItem> items,
                                                          Int64? totalBytes,
                                                          Func<Exception, String?> classifyError,
                                                          CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(classifyError);

        var downloaded = 0;
        var failed = 0;
        foreach (var item in items)
        {
            try
            {
                await item.DownloadAsync(null, cancellationToken);
                downloaded++;
            }
            catch (Exception exception)
            {
                if (classifyError(exception) is null)
                {
                    throw;
                }

                failed++;
            }
        }

        return new(downloaded, failed);
    }

    public Task RunSingleAsync(String fileName,
                               Int64? sizeBytes,
                               Func<IProgress<Int64>?, CancellationToken, Task> downloadAsync,
                               CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(downloadAsync);
        return downloadAsync(null, cancellationToken);
    }
}
