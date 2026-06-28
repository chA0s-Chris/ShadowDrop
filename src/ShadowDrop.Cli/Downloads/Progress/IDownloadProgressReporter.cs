// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Downloads.Progress;

/// <summary>
/// Describes one queued file download, including the work that performs the download while reporting durable plaintext bytes.
/// </summary>
/// <param name="FileName">The display name of the file.</param>
/// <param name="SizeBytes">The declared plaintext size in bytes, or <see langword="null"/> when unknown.</param>
/// <param name="OutputPath">The queue-relative output path, used for the lifecycle output.</param>
/// <param name="DownloadAsync">Performs the download, reporting cumulative durable plaintext bytes to the supplied progress sink.</param>
internal sealed record QueueDownloadItem(
    String FileName,
    Int64? SizeBytes,
    String OutputPath,
    Func<IProgress<Int64>?, CancellationToken, Task> DownloadAsync);

/// <summary>
/// Summarizes the outcome of a queue download.
/// </summary>
/// <param name="Downloaded">The number of files downloaded successfully.</param>
/// <param name="Failed">The number of files that failed.</param>
internal sealed record DownloadQueueSummary(Int32 Downloaded, Int32 Failed);

/// <summary>
/// Reports download progress and lifecycle output to standard error for non-interactive single-file and queue downloads.
/// </summary>
internal interface IDownloadProgressReporter
{
    /// <summary>
    /// Runs a queue download, emitting per-file lifecycle output and a final summary while continuing past individual failures.
    /// </summary>
    /// <param name="items">The queued files in processing order.</param>
    /// <param name="totalBytes">The summed declared queue size in bytes, or <see langword="null"/> when not fully known.</param>
    /// <param name="classifyError">
    /// Maps a thrown exception to a user-facing failure message; returning <see langword="null"/> rethrows the exception unchanged.
    /// </param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The counts of downloaded and failed files.</returns>
    Task<DownloadQueueSummary> RunQueueAsync(IReadOnlyList<QueueDownloadItem> items,
                                             Int64? totalBytes,
                                             Func<Exception, String?> classifyError,
                                             CancellationToken cancellationToken);

    /// <summary>
    /// Runs a non-interactive single-file download, emitting start, progress, and completion output.
    /// </summary>
    /// <param name="fileName">The display name of the file.</param>
    /// <param name="sizeBytes">The declared plaintext size in bytes, or <see langword="null"/> when unknown.</param>
    /// <param name="downloadAsync">Performs the download, reporting cumulative durable plaintext bytes to the supplied progress sink.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task RunSingleAsync(String fileName,
                        Int64? sizeBytes,
                        Func<IProgress<Int64>?, CancellationToken, Task> downloadAsync,
                        CancellationToken cancellationToken);
}
