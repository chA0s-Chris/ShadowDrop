// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Uploads;

using ShadowDrop.Cli.Results;
using ShadowDrop.Cli.Uploads.Progress;
using ShadowDrop.Contracts;
using ShadowDrop.Crypto;

internal sealed class UploadCommandExecutor
{
    private readonly HttpClient _httpClient;

    public UploadCommandExecutor(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<UploadExecutionResult> ExecuteAsync(LocalUploadPlan plan,
                                                          Uri serverUrl,
                                                          String uploadToken,
                                                          IUploadProgressReporter progressReporter,
                                                          CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(serverUrl);
        ArgumentNullException.ThrowIfNull(progressReporter);
        ArgumentException.ThrowIfNullOrWhiteSpace(uploadToken);

        if (plan.Files.IsEmpty)
        {
            return new([], null, false);
        }

        var uploadApiClient = new UploadApiClient(_httpClient);
        UploadCapabilitiesResponse capabilities;
        try
        {
            capabilities = await uploadApiClient.GetCapabilitiesAsync(serverUrl, uploadToken, cancellationToken);
        }
        catch (UploadCommandException exception)
        {
            await progressReporter.ReportBatchErrorAsync(exception.Message, cancellationToken);
            return new([], null, false, exception.Message);
        }

        var oversizedFiles = FindOversizedFiles(plan.Files, capabilities.MaxFilePayloadBytes);
        if (oversizedFiles.Count > 0)
        {
            foreach (var error in oversizedFiles)
            {
                await progressReporter.ReportFileFailureAsync(new(error.File.Name, error.FileNumber, plan.Files.Length, error.UploadSizeBytes ?? 0),
                                                              error.ErrorMessage ?? "Upload failed.",
                                                              cancellationToken);
            }

            return new(oversizedFiles, null, false);
        }

        using var shareSecret = ShareSecret.Generate();
        List<UploadFileExecutionResult> results = [];

        foreach (var file in plan.Files)
        {
            UploadFilePlan uploadFilePlan;
            try
            {
                uploadFilePlan = await CreatePlanAsync(file, uploadApiClient, serverUrl, uploadToken, cancellationToken);
            }
            catch (Exception exception) when (ClassifyUploadException(exception) is { } message)
            {
                await progressReporter.ReportFileFailureAsync(new(file.File.Name, file.FileNumber, plan.Files.Length, file.EncryptedLength),
                                                              message,
                                                              cancellationToken);
                results.Add(new(file.File, file.FileNumber, null, message));
                break;
            }

            var upload =
                await progressReporter.RunFileAsync(new(file.File.Name, file.FileNumber, plan.Files.Length, uploadFilePlan.Metadata.EncryptedLength),
                                                    (progressSink, token) =>
                                                        uploadApiClient.UploadAsync(serverUrl,
                                                                                    uploadToken,
                                                                                    uploadFilePlan,
                                                                                    shareSecret,
                                                                                    progressSink,
                                                                                    token),
                                                    ClassifyUploadException,
                                                    cancellationToken);
            if (upload.ErrorMessage is null)
            {
                results.Add(new(file.File, file.FileNumber, upload.Value, null));
                continue;
            }

            results.Add(new(file.File, file.FileNumber, null, upload.ErrorMessage));
            break;
        }

        var allSucceeded = results.All(static result => result.UploadedFileId is not null);
        return new(results,
                   allSucceeded ? Convert.ToHexStringLower(shareSecret.KeyMaterial) : null,
                   allSucceeded);
    }

    private static String? ClassifyUploadException(Exception exception) => exception switch
    {
        UploadCommandException uploadException => uploadException.Message,
        UnauthorizedAccessException => "File is unreadable.",
        IOException => "File is unreadable.",
        _ => null
    };

    private static async Task<UploadFilePlan> CreatePlanAsync(LocalUploadFile file,
                                                              UploadApiClient uploadApiClient,
                                                              Uri serverUrl,
                                                              String uploadToken,
                                                              CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);

        // Preflight snapshotted the length that the metadata and Content-Length are derived from, but the bytes
        // are streamed later. Re-stat before reserving a file ID so a file changed in between fails cheaply
        // instead of burning a reservation on a request that cannot satisfy its own Content-Length. This narrows
        // the window; it cannot close it.
        RevalidatePreflightSnapshot(file);

        var fileId = await uploadApiClient.ReserveFileIdAsync(serverUrl, uploadToken, cancellationToken);
        var kdfSalt = FileEncryptionContext.GenerateKdfSalt();
        var encryptionContext = new FileEncryptionContext(fileId, kdfSalt);
        var metadata = new UploadMetadataPayload(fileId,
                                                 file.File.Name,
                                                 file.PlaintextLength,
                                                 file.EncryptedLength,
                                                 "application/octet-stream",
                                                 FormatConstants.EncryptionFormatVersion,
                                                 FormatConstants.Aes256GcmAlgorithmId,
                                                 LocalUploadPlanner.ChunkSize,
                                                 file.ChunkCount,
                                                 Convert.ToBase64String(kdfSalt),
                                                 null);
        return new(file.File, file.RecursiveRootPath, fileId, encryptionContext, metadata, LocalUploadPlanner.ChunkSize);
    }

    private static IReadOnlyList<UploadFileExecutionResult> FindOversizedFiles(IReadOnlyList<LocalUploadFile> files, Int64 maxFilePayloadBytes)
    {
        List<UploadFileExecutionResult> errors = [];
        foreach (var file in files)
        {
            if (file.EncryptedLength > maxFilePayloadBytes)
            {
                errors.Add(new(file.File,
                               file.FileNumber,
                               null,
                               $"{file.File.Name} exceeds the maximum upload size. Upload size is {file.EncryptedLength} bytes; maximum is {maxFilePayloadBytes} bytes.",
                               file.EncryptedLength,
                               maxFilePayloadBytes));
            }
        }

        return errors;
    }

    private static void RevalidatePreflightSnapshot(LocalUploadFile file)
    {
        // FileInfo caches Exists/Length; Refresh() is what makes this a re-stat rather than a replay of preflight.
        file.File.Refresh();

        if (!file.File.Exists)
        {
            throw new UploadCommandException("File is missing.");
        }

        // Gate before opening, as preflight does: a file swapped for a FIFO would otherwise block the upload
        // here instead of failing it.
        var target = file.File.ResolveLinkTarget(true) as FileInfo ?? file.File;
        if (target.Length <= 0)
        {
            throw new UploadCommandException($"{file.File.Name} changed while preparing the upload.");
        }

        // Opened rather than stat'ed, for the same reason preflight measures through a handle: FileInfo.Length
        // reports a symlink's own size, so comparing it would fail every link and pass nothing useful.
        using var probe = file.File.OpenRead();
        if (probe.Length != file.PlaintextLength)
        {
            throw new UploadCommandException($"{file.File.Name} changed while preparing the upload.");
        }
    }
}

internal sealed record UploadExecutionResult(
    IReadOnlyList<UploadFileExecutionResult> Files,
    String? ShareSecretHex,
    Boolean AllSucceeded,
    String? BatchErrorMessage = null)
{
    // Materialized once: callers read this repeatedly, and a getter that rebuilt the list per access
    // would hand out a fresh instance every time.
    public IReadOnlyList<UploadFailure> Failures { get; } = BuildFailures(Files, BatchErrorMessage);

    public IReadOnlyList<Guid> UploadedFileIds
    {
        get
        {
            List<Guid> ids = [];
            foreach (var result in Files)
            {
                if (result.UploadedFileId is { } fileId)
                {
                    ids.Add(fileId);
                }
            }

            return [.. ids];
        }
    }

    private static List<UploadFailure> BuildFailures(IReadOnlyList<UploadFileExecutionResult> files,
                                                     String? batchErrorMessage)
    {
        List<UploadFailure> failures = [];
        if (batchErrorMessage is not null)
        {
            failures.Add(new(null, null, batchErrorMessage, null, null));
        }

        failures.AddRange(files.Where(static result => result.UploadedFileId is null)
                               .Select(static result => new UploadFailure(result.FileNumber,
                                                                          result.File.Name,
                                                                          result.ErrorMessage ?? "Upload failed.",
                                                                          result.UploadSizeBytes,
                                                                          result.MaxFilePayloadBytes)));
        return failures;
    }
}

internal sealed record UploadFileExecutionResult(
    FileInfo File,
    Int32 FileNumber,
    Guid? UploadedFileId,
    String? ErrorMessage,
    Int64? UploadSizeBytes = null,
    Int64? MaxFilePayloadBytes = null);
