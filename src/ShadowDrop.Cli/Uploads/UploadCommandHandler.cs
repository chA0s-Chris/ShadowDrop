// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Uploads;

using ShadowDrop.Cli.Configuration;
using ShadowDrop.Contracts;
using ShadowDrop.Crypto;
using System.Text.Json;

internal sealed class UploadCommandHandler(
    CliConfigurationResolver configurationResolver,
    HttpClient httpClient,
    TextWriter standardOut,
    TextWriter standardError)
{
    private const Int32 ChunkSize = 1024 * 1024;

    public async Task<Int32> ExecuteAsync(UploadCommandOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);

        CliResolvedConfiguration configuration;
        try
        {
            configuration = configurationResolver.Resolve(options.ServerUrlOverride, options.UploadTokenOverride);
        }
        catch (IOException)
        {
            await standardError.WriteLineAsync("Configuration file invalid or unreadable.");
            return 1;
        }
        catch (UnauthorizedAccessException)
        {
            await standardError.WriteLineAsync("Configuration file invalid or unreadable.");
            return 1;
        }
        catch (JsonException)
        {
            await standardError.WriteLineAsync("Configuration file invalid or unreadable.");
            return 1;
        }

        if (!Uri.TryCreate(configuration.ServerUrl, UriKind.Absolute, out var serverUrl)
            || ((serverUrl.Scheme != Uri.UriSchemeHttp) && (serverUrl.Scheme != Uri.UriSchemeHttps)))
        {
            await standardError.WriteLineAsync("Server URL invalid or missing.");
            return 1;
        }

        if (String.IsNullOrWhiteSpace(configuration.UploadToken))
        {
            await standardError.WriteLineAsync("Authentication token invalid or missing.");
            return 1;
        }

        using var shareSecret = ShareSecret.Generate();
        var uploadApiClient = new UploadApiClient(httpClient);
        var allSucceeded = true;

        for (var index = 0; index < options.Files.Length; index++)
        {
            try
            {
                var plan = await CreatePlanAsync(options.Files[index], uploadApiClient, serverUrl, configuration.UploadToken, cancellationToken);
                var uploadedFileId = await uploadApiClient.UploadAsync(serverUrl, configuration.UploadToken, plan, shareSecret, cancellationToken);
                await standardOut.WriteLineAsync(uploadedFileId.ToString());
                await standardError.WriteLineAsync($"Uploaded file {index + 1} of {options.Files.Length}.");
            }
            catch (UploadCommandException exception)
            {
                allSucceeded = false;
                await standardError.WriteLineAsync($"File {index + 1} failed: {exception.Message}");
            }
            catch (UnauthorizedAccessException)
            {
                allSucceeded = false;
                await standardError.WriteLineAsync($"File {index + 1} failed: File is unreadable.");
            }
            catch (IOException)
            {
                allSucceeded = false;
                await standardError.WriteLineAsync($"File {index + 1} failed: File is unreadable.");
            }
        }

        if (allSucceeded && options.OutputSecret)
        {
            await standardOut.WriteLineAsync($"secret:{Convert.ToHexStringLower(shareSecret.KeyMaterial)}");
        }

        return allSucceeded ? 0 : 1;
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
