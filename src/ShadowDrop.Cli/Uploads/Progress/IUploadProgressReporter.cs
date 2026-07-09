// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Uploads.Progress;

/// <summary>
/// Describes one file upload for lifecycle and progress output.
/// </summary>
/// <param name="FileName">The local file name to display.</param>
/// <param name="Position">The one-based file position in the requested batch.</param>
/// <param name="Total">The requested batch file count.</param>
/// <param name="TotalBytes">The encrypted HTTP payload byte count.</param>
internal sealed record UploadProgressFile(String FileName, Int32 Position, Int32 Total, Int64 TotalBytes);

/// <summary>
/// Carries byte progress and retry lifecycle callbacks into the upload streaming path.
/// </summary>
/// <param name="Bytes">Reports cumulative encrypted bytes written for the current attempt.</param>
/// <param name="RetryingAsync">Reports that a retry attempt is about to restart streaming for the same file.</param>
internal sealed record UploadProgressSink(IProgress<Int64> Bytes, Func<Int32, CancellationToken, Task> RetryingAsync);

/// <summary>
/// The outcome of one upload wrapped by a progress reporter.
/// </summary>
/// <typeparam name="T">The successful upload result type.</typeparam>
/// <param name="Value">The successful upload result, or <see langword="default"/> when the upload failed.</param>
/// <param name="ErrorMessage">The classified failure message, or <see langword="null"/> when the upload succeeded.</param>
internal sealed record UploadProgressResult<T>(T? Value, String? ErrorMessage);

/// <summary>
/// Reports upload lifecycle and byte progress. Implementations write only to standard error, or nowhere for JSON mode.
/// </summary>
internal interface IUploadProgressReporter
{
    Task ReportBatchErrorAsync(String message, CancellationToken cancellationToken);

    Task ReportFileFailureAsync(UploadProgressFile file, String message, CancellationToken cancellationToken);

    Task<UploadProgressResult<T>> RunFileAsync<T>(UploadProgressFile file,
                                                  Func<UploadProgressSink?, CancellationToken, Task<T>> uploadAsync,
                                                  Func<Exception, String?> classifyError,
                                                  CancellationToken cancellationToken);
}
