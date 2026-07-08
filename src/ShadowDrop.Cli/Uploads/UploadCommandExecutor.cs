// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Uploads;

using ShadowDrop.Cli.Results;
using ShadowDrop.Contracts;
using ShadowDrop.Crypto;

internal sealed class UploadCommandExecutor(HttpClient httpClient)
{
    private const Int32 ChunkSize = 1024 * 1024;

    public async Task<UploadExecutionResult> ExecuteAsync(IReadOnlyList<FileInfo> files, Uri serverUrl, String uploadToken, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(serverUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(uploadToken);

        if (files.Count == 0)
        {
            return new([], null, false);
        }

        var uploadApiClient = new UploadApiClient(httpClient);
        var preflight = PreflightFiles(files);
        if (preflight.Errors.Count > 0)
        {
            return new(preflight.Errors, null, false);
        }

        UploadCapabilitiesResponse capabilities;
        try
        {
            capabilities = await uploadApiClient.GetCapabilitiesAsync(serverUrl, uploadToken, cancellationToken);
        }
        catch (UploadCommandException exception)
        {
            return new([], null, false, exception.Message);
        }

        var oversizedFiles = FindOversizedFiles(preflight.Files, capabilities.MaxFilePayloadBytes);
        if (oversizedFiles.Count > 0)
        {
            return new(oversizedFiles, null, false);
        }

        using var shareSecret = ShareSecret.Generate();
        List<UploadFileExecutionResult> results = [];

        foreach (var file in preflight.Files)
        {
            try
            {
                var plan = await CreatePlanAsync(file, uploadApiClient, serverUrl, uploadToken, cancellationToken);
                var uploadedFileId = await uploadApiClient.UploadAsync(serverUrl, uploadToken, plan, shareSecret, cancellationToken);
                results.Add(new(file.File, file.FileNumber, uploadedFileId, null));
            }
            catch (UploadCommandException exception)
            {
                results.Add(new(file.File, file.FileNumber, null, exception.Message));
                break;
            }
            catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
            {
                results.Add(new(file.File, file.FileNumber, null, "File is unreadable."));
                break;
            }
        }

        var allSucceeded = results.All(static result => result.UploadedFileId is not null);
        return new(results,
                   allSucceeded ? Convert.ToHexStringLower(shareSecret.KeyMaterial) : null,
                   allSucceeded);
    }

    private static async Task<UploadFilePlan> CreatePlanAsync(PreflightedUploadFile file,
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
                                                 ChunkSize,
                                                 file.ChunkCount,
                                                 Convert.ToBase64String(kdfSalt),
                                                 null);
        return new(file.File, fileId, encryptionContext, metadata, ChunkSize);
    }

    private static IReadOnlyList<UploadFileExecutionResult> FindOversizedFiles(IReadOnlyList<PreflightedUploadFile> files, Int64 maxFilePayloadBytes)
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

    private static UploadPreflightResult PreflightFiles(IReadOnlyList<FileInfo> files)
    {
        List<PreflightedUploadFile> preflightedFiles = [];
        List<UploadFileExecutionResult> errors = [];

        for (var index = 0; index < files.Count; index++)
        {
            var file = files[index];
            var fileNumber = index + 1;
            try
            {
                if (!file.Exists)
                {
                    errors.Add(new(file, fileNumber, null, "File is missing."));
                    continue;
                }

                var plaintextLength = file.Length;
                if (plaintextLength <= 0)
                {
                    errors.Add(new(file, fileNumber, null, "File is empty."));
                    continue;
                }

                using var probe = file.OpenRead();
                _ = probe.Length;

                var chunkCount = checked(((plaintextLength - 1) / ChunkSize) + 1);
                var encryptedLength = checked(plaintextLength + (chunkCount * EncryptedChunk.AuthenticationTagLength));
                preflightedFiles.Add(new(file, fileNumber, plaintextLength, chunkCount, encryptedLength));
            }
            catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
            {
                errors.Add(new(file, fileNumber, null, "File is unreadable."));
            }
            catch (OverflowException)
            {
                errors.Add(new(file, fileNumber, null, $"{file.Name} exceeds the maximum upload size."));
            }
        }

        return new(preflightedFiles, errors);
    }

    private static void RevalidatePreflightSnapshot(PreflightedUploadFile file)
    {
        // FileInfo caches Exists/Length; Refresh() is what makes this a re-stat rather than a replay of preflight.
        file.File.Refresh();

        if (!file.File.Exists)
        {
            throw new UploadCommandException("File is missing.");
        }

        if (file.File.Length != file.PlaintextLength)
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

    public IReadOnlyList<Guid> UploadedFileIds => Files.Where(static result => result.UploadedFileId is not null)
                                                       .Select(static result => result.UploadedFileId!.Value)
                                                       .ToArray();

    private static IReadOnlyList<UploadFailure> BuildFailures(IReadOnlyList<UploadFileExecutionResult> files,
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

internal sealed record UploadPreflightResult(
    IReadOnlyList<PreflightedUploadFile> Files,
    IReadOnlyList<UploadFileExecutionResult> Errors);

internal sealed record PreflightedUploadFile(
    FileInfo File,
    Int32 FileNumber,
    Int64 PlaintextLength,
    Int64 ChunkCount,
    Int64 EncryptedLength);
