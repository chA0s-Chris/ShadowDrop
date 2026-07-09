// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Uploads.Progress;

/// <summary>
/// Suppresses upload progress and lifecycle output for JSON mode while still running the upload workflow.
/// </summary>
internal sealed class NullUploadProgressReporter : IUploadProgressReporter
{
    private NullUploadProgressReporter() { }
    public static NullUploadProgressReporter Instance { get; } = new();

    public Task ReportBatchErrorAsync(String message, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task ReportFileFailureAsync(UploadProgressFile file, String message, CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task<UploadProgressResult<T>> RunFileAsync<T>(UploadProgressFile file,
                                                               Func<UploadProgressSink?, CancellationToken, Task<T>> uploadAsync,
                                                               Func<Exception, String?> classifyError,
                                                               CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(uploadAsync);
        ArgumentNullException.ThrowIfNull(classifyError);

        try
        {
            return new(await uploadAsync(null, cancellationToken), null);
        }
        catch (Exception exception)
        {
            var message = classifyError(exception);
            if (message is null)
            {
                throw;
            }

            return new(default, message);
        }
    }
}
