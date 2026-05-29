// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Uploads;

using ShadowDrop.Cli.Configuration;
using System.Text.Json;

internal sealed class UploadCommandHandler(
    CliConfigurationResolver configurationResolver,
    HttpClient httpClient,
    TextWriter standardOut,
    TextWriter standardError)
{
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

        var executor = new UploadCommandExecutor(httpClient);
        var executionResult = await executor.ExecuteAsync(options.Files, serverUrl, configuration.UploadToken, cancellationToken);

        for (var index = 0; index < executionResult.Files.Count; index++)
        {
            var fileResult = executionResult.Files[index];
            if (fileResult.UploadedFileId is not null)
            {
                await standardOut.WriteLineAsync(fileResult.UploadedFileId.Value.ToString());
                await standardError.WriteLineAsync($"Uploaded file {index + 1} of {options.Files.Length}.");
                continue;
            }

            await standardError.WriteLineAsync($"File {index + 1} failed: {fileResult.ErrorMessage}");
        }

        if (executionResult.AllSucceeded && options.OutputSecret && !String.IsNullOrWhiteSpace(executionResult.ShareSecretHex))
        {
            await standardOut.WriteLineAsync($"secret:{executionResult.ShareSecretHex}");
        }

        return executionResult.AllSucceeded ? 0 : 1;
    }
}
