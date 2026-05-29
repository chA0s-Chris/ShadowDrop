// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Uploads;

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

        using var shareSecret = ShareSecret.Generate();
        var uploadApiClient = new UploadApiClient(httpClient);
        List<UploadFileExecutionResult> results = [];

        foreach (var file in files)
        {
            try
            {
                var plan = await CreatePlanAsync(file, uploadApiClient, serverUrl, uploadToken, cancellationToken);
                var uploadedFileId = await uploadApiClient.UploadAsync(serverUrl, uploadToken, plan, shareSecret, cancellationToken);
                results.Add(new(file, uploadedFileId, null));
            }
            catch (UploadCommandException exception)
            {
                results.Add(new(file, null, exception.Message));
            }
            catch (UnauthorizedAccessException)
            {
                results.Add(new(file, null, "File is unreadable."));
            }
            catch (IOException)
            {
                results.Add(new(file, null, "File is unreadable."));
            }
        }

        var allSucceeded = results.All(static result => result.UploadedFileId is not null);
        return new(results,
                   allSucceeded ? Convert.ToHexStringLower(shareSecret.KeyMaterial) : null,
                   allSucceeded);
    }

    private static async Task<UploadFilePlan> CreatePlanAsync(FileInfo file,
                                                              UploadApiClient uploadApiClient,
                                                              Uri serverUrl,
                                                              String uploadToken,
                                                              CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);

        if (!file.Exists)
        {
            throw new UploadCommandException("File is missing.");
        }

        if (file.Length <= 0)
        {
            throw new UploadCommandException("File is empty.");
        }

        await using var probe = file.OpenRead();
        _ = probe.Length;

        var fileId = await uploadApiClient.ReserveFileIdAsync(serverUrl, uploadToken, cancellationToken);
        var plaintextLength = file.Length;
        var chunkCount = checked(((plaintextLength - 1) / ChunkSize) + 1);
        var encryptedLength = checked(plaintextLength + (chunkCount * EncryptedChunk.AuthenticationTagLength));
        var kdfSalt = FileEncryptionContext.GenerateKdfSalt();
        var encryptionContext = new FileEncryptionContext(fileId, kdfSalt);
        var metadata = new UploadMetadataPayload(fileId,
                                                 file.Name,
                                                 plaintextLength,
                                                 encryptedLength,
                                                 "application/octet-stream",
                                                 FormatConstants.EncryptionFormatVersion,
                                                 FormatConstants.Aes256GcmAlgorithmId,
                                                 ChunkSize,
                                                 chunkCount,
                                                 Convert.ToBase64String(kdfSalt),
                                                 null);
        return new(file, fileId, encryptionContext, metadata, ChunkSize);
    }
}

internal sealed record UploadExecutionResult(
    IReadOnlyList<UploadFileExecutionResult> Files,
    String? ShareSecretHex,
    Boolean AllSucceeded)
{
    public IReadOnlyList<Guid> UploadedFileIds => Files.Where(static result => result.UploadedFileId is not null)
                                                       .Select(static result => result.UploadedFileId!.Value)
                                                       .ToArray();
}

internal sealed record UploadFileExecutionResult(FileInfo File, Guid? UploadedFileId, String? ErrorMessage);
