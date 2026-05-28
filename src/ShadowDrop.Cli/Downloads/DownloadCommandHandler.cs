// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Downloads;

using ShadowDrop.Cli.Configuration;
using ShadowDrop.Contracts;
using ShadowDrop.Crypto;
using ShadowDrop.Queue;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

internal sealed class DownloadCommandHandler(
    CliConfigurationResolver configurationResolver,
    HttpClient httpClient,
    Stream standardOutStream,
    TextWriter standardError)
{
    private const String SecretPrefix = "secret:";

    public async Task<Int32> ExecuteAsync(DownloadCommandOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);

        Byte[]? shareKeyBytes = null;
        try
        {
            shareKeyBytes = await ResolveShareKeyBytesAsync(options, cancellationToken);
            if (shareKeyBytes is null)
            {
                await standardError.WriteLineAsync("Share key invalid or missing.");
                return 1;
            }

            if (options.QueuePath is not null)
            {
                if (!String.IsNullOrWhiteSpace(options.ShareId)
                    || !String.IsNullOrWhiteSpace(options.FileId)
                    || !String.IsNullOrWhiteSpace(options.ServerUrlOverride))
                {
                    await standardError.WriteLineAsync("The --queue option cannot be combined with a share id, --file, or --server-url.");
                    return 1;
                }

                return await ExecuteQueueAsync(options.QueuePath, shareKeyBytes, options.BearerToken, cancellationToken);
            }

            if (String.IsNullOrWhiteSpace(options.ShareId))
            {
                await standardError.WriteLineAsync("Specify either a share id or --queue.");
                return 1;
            }

            var shareReference = ResolveShareReference(options.ShareId, options.ServerUrlOverride);
            var manifestClient = new ShareManifestClient(httpClient);
            var manifest = await manifestClient.GetAsync(shareReference.ServerUrl, shareReference.ShareId, options.BearerToken, cancellationToken);
            var file = SelectDirectDownloadFile(manifest, options.FileId);
            await DownloadToStreamAsync(shareReference.ServerUrl, shareReference.ShareId, file, shareKeyBytes, options.BearerToken, standardOutStream,
                                        cancellationToken);
            return 0;
        }
        catch (DownloadCommandException exception)
        {
            await standardError.WriteLineAsync(exception.Message);
            return 1;
        }
        catch (IOException)
        {
            await standardError.WriteLineAsync("Download failed due to a local I/O error.");
            return 1;
        }
        catch (UnauthorizedAccessException)
        {
            await standardError.WriteLineAsync("Download failed due to a local I/O error.");
            return 1;
        }
        finally
        {
            if (shareKeyBytes is not null)
            {
                CryptographicOperations.ZeroMemory(shareKeyBytes);
            }
        }
    }

    private static Byte[] DecodeBase64(String? base64Value, String errorMessage)
    {
        if (String.IsNullOrWhiteSpace(base64Value))
        {
            throw new DownloadCommandException(errorMessage);
        }

        try
        {
            return Convert.FromBase64String(base64Value);
        }
        catch (FormatException)
        {
            throw new DownloadCommandException(errorMessage);
        }
    }

    private static Byte[] DecodeShareKey(String shareKey) => DecodeShareKey(shareKey.AsSpan());

    private static Byte[] DecodeShareKey(ReadOnlySpan<Char> shareKey)
    {
        var trimmedValue = TrimAsciiWhitespace(shareKey);
        if (trimmedValue.StartsWith(SecretPrefix, StringComparison.OrdinalIgnoreCase))
        {
            trimmedValue = TrimAsciiWhitespace(trimmedValue[SecretPrefix.Length..]);
        }

        try
        {
            var keyBytes = Convert.FromHexString(trimmedValue);
            if (keyBytes.Length != 32)
            {
                CryptographicOperations.ZeroMemory(keyBytes);
                throw new DownloadCommandException("Share key invalid or missing.");
            }

            return keyBytes;
        }
        catch (FormatException)
        {
            throw new DownloadCommandException("Share key invalid or missing.");
        }
    }

    private static Guid ParseFileId(String? fileId)
    {
        if (!Guid.TryParse(fileId, out var parsedFileId) || parsedFileId == Guid.Empty)
        {
            throw new DownloadCommandException("Share metadata invalid or missing.");
        }

        return parsedFileId;
    }

    private static ShareManifestFileContract SelectDirectDownloadFile(ShareManifestContract manifest, String? fileId)
    {
        if (!String.IsNullOrWhiteSpace(fileId))
        {
            var match = manifest.Files!.SingleOrDefault(file => String.Equals(file.FileId, fileId, StringComparison.Ordinal));
            return match ?? throw new DownloadCommandException("Requested file not found in share.");
        }

        return manifest.Files!.Count == 1
            ? manifest.Files[0]
            : throw new DownloadCommandException("Share contains multiple files; specify --file.");
    }

    private static ShareManifestFileContract SelectQueuedFile(ShareManifestContract manifest, QueueFileEntry entry)
    {
        var match = manifest.Files!.SingleOrDefault(file => String.Equals(file.FileId, entry.FileId, StringComparison.Ordinal));
        if (match is null)
        {
            throw new DownloadCommandException("Requested file not found in share.");
        }

        if (!String.Equals(match.FileName, entry.FileName, StringComparison.Ordinal) || match.Length != entry.Length)
        {
            throw new DownloadCommandException("Queue entry does not match share metadata.");
        }

        return match;
    }

    private static ReadOnlySpan<Char> TrimAsciiWhitespace(ReadOnlySpan<Char> value)
    {
        while (value.Length > 0 && Char.IsWhiteSpace(value[0]))
        {
            value = value[1..];
        }

        while (value.Length > 0 && Char.IsWhiteSpace(value[^1]))
        {
            value = value[..^1];
        }

        return value;
    }

    private async Task DownloadToFileAsync(Uri serverUrl,
                                           String shareId,
                                           ShareManifestFileContract file,
                                           Byte[] shareKeyBytes,
                                           String? bearerToken,
                                           String outputPath,
                                           CancellationToken cancellationToken)
    {
        var directoryPath = Path.GetDirectoryName(outputPath);
        if (!String.IsNullOrWhiteSpace(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        var partialPath = $"{outputPath}.shadowdrop-partial";
        try
        {
            await using var destination = new FileStream(partialPath,
                                                         FileMode.Create,
                                                         FileAccess.Write,
                                                         FileShare.None,
                                                         81920,
                                                         FileOptions.Asynchronous);
            await DownloadToStreamAsync(serverUrl, shareId, file, shareKeyBytes, bearerToken, destination, cancellationToken);
            await destination.FlushAsync(cancellationToken);
            File.Move(partialPath, outputPath, true);
        }
        catch
        {
            if (File.Exists(partialPath))
            {
                File.Delete(partialPath);
            }

            throw;
        }
    }

    private async Task DownloadToStreamAsync(Uri serverUrl,
                                             String shareId,
                                             ShareManifestFileContract file,
                                             Byte[] shareKeyBytes,
                                             String? bearerToken,
                                             Stream destination,
                                             CancellationToken cancellationToken)
    {
        var fileId = ParseFileId(file.FileId);
        var downloadUri = ShareDownloadUriFactory.CreateFileUri(serverUrl, shareId, fileId);
        Byte[]? kdfSalt = null;
        try
        {
            kdfSalt = DecodeBase64(file.KdfSalt, "Share metadata invalid or missing.");
            using var shareSecret = ShareSecret.FromBytes(shareKeyBytes);
            using var session = new CliDownloadSession(httpClient,
                                                       downloadUri,
                                                       destination,
                                                       shareSecret,
                                                       new(fileId, kdfSalt),
                                                       bearerToken);
            await session.DownloadAsync(cancellationToken);
        }
        catch (FormatException)
        {
            throw new DownloadCommandException("Share metadata invalid or missing.");
        }
        catch (ArgumentException)
        {
            throw new DownloadCommandException("Share metadata invalid or missing.");
        }
        catch (CryptographicException)
        {
            throw new DownloadCommandException("Decryption failed.");
        }
        catch (HttpRequestException)
        {
            throw new DownloadCommandException("Server connection failed.");
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new DownloadCommandException("Server connection failed.", exception);
        }
        catch (InvalidDataException)
        {
            throw new DownloadCommandException("Decryption failed.");
        }
        finally
        {
            if (kdfSalt is not null)
            {
                CryptographicOperations.ZeroMemory(kdfSalt);
            }
        }
    }

    private async Task<Int32> ExecuteQueueAsync(FileInfo queuePath, Byte[] shareKeyBytes, String? bearerToken, CancellationToken cancellationToken)
    {
        var queue = await LoadQueueAsync(queuePath, cancellationToken);
        var manifestClient = new ShareManifestClient(httpClient);
        Dictionary<String, ShareManifestContract> manifestCache = [];
        List<String> results = [];
        var allSucceeded = true;

        foreach (var entry in queue.Files!)
        {
            var summaryLabel = entry.FileName ?? entry.FileId ?? "unknown";
            try
            {
                var shareReference = ResolveShareReference(entry.ShareId!, entry.ServerUrl);
                var manifest = await GetManifestAsync(manifestClient, manifestCache, shareReference, bearerToken, cancellationToken);
                var file = SelectQueuedFile(manifest, entry);
                await DownloadToFileAsync(shareReference.ServerUrl, shareReference.ShareId, file, shareKeyBytes, bearerToken, entry.OutputPath!,
                                          cancellationToken);
                results.Add($"SUCCESS {summaryLabel} -> {entry.OutputPath}");
            }
            catch (DownloadCommandException exception)
            {
                allSucceeded = false;
                results.Add($"FAILED {summaryLabel} -> {entry.OutputPath}: {exception.Message}");
            }
            catch (IOException)
            {
                allSucceeded = false;
                results.Add($"FAILED {summaryLabel} -> {entry.OutputPath}: Download failed due to a local I/O error.");
            }
            catch (UnauthorizedAccessException)
            {
                allSucceeded = false;
                results.Add($"FAILED {summaryLabel} -> {entry.OutputPath}: Download failed due to a local I/O error.");
            }
        }

        foreach (var result in results)
        {
            await standardError.WriteLineAsync(result);
        }

        return allSucceeded ? 0 : 1;
    }

    private async Task<ShareManifestContract> GetManifestAsync(ShareManifestClient manifestClient,
                                                               IDictionary<String, ShareManifestContract> manifestCache,
                                                               ShareReference shareReference,
                                                               String? bearerToken,
                                                               CancellationToken cancellationToken)
    {
        var cacheKey = $"{shareReference.ServerUrl}|{shareReference.ShareId}";
        if (manifestCache.TryGetValue(cacheKey, out var cachedManifest))
        {
            return cachedManifest;
        }

        var manifest = await manifestClient.GetAsync(shareReference.ServerUrl, shareReference.ShareId, bearerToken, cancellationToken);
        manifestCache[cacheKey] = manifest;
        return manifest;
    }

    private async Task<QueueFile> LoadQueueAsync(FileInfo queuePath, CancellationToken cancellationToken)
    {
        try
        {
            var json = await File.ReadAllTextAsync(queuePath.FullName, cancellationToken);
            return QueueFileParser.Parse(json);
        }
        catch (QueueFileValidationException exception)
        {
            foreach (var error in exception.Errors)
            {
                await standardError.WriteLineAsync($"{error.Path}: {error.Message}");
            }

            throw new DownloadCommandException("The queue file is invalid.");
        }
        catch (JsonException)
        {
            throw new DownloadCommandException("The queue file is invalid.");
        }
        catch (ArgumentException)
        {
            throw new DownloadCommandException("The queue file is invalid.");
        }
        catch (IOException)
        {
            throw new DownloadCommandException("The queue file is invalid or unreadable.");
        }
        catch (UnauthorizedAccessException)
        {
            throw new DownloadCommandException("The queue file is invalid or unreadable.");
        }
    }

    private CliResolvedConfiguration ResolveConfiguration(String? serverUrlOverride)
    {
        try
        {
            return configurationResolver.Resolve(serverUrlOverride, null);
        }
        catch (IOException)
        {
            throw new DownloadCommandException("Configuration file invalid or unreadable.");
        }
        catch (UnauthorizedAccessException)
        {
            throw new DownloadCommandException("Configuration file invalid or unreadable.");
        }
        catch (JsonException)
        {
            throw new DownloadCommandException("Configuration file invalid or unreadable.");
        }
    }

    private async Task<Byte[]?> ResolveShareKeyBytesAsync(DownloadCommandOptions options, CancellationToken cancellationToken)
    {
        if (!String.IsNullOrWhiteSpace(options.ShareKey))
        {
            return DecodeShareKey(options.ShareKey);
        }

        if (options.ShareKeyFile is null)
        {
            return null;
        }

        Byte[]? fileBytes = null;
        Char[]? fileChars = null;
        try
        {
            fileBytes = await File.ReadAllBytesAsync(options.ShareKeyFile.FullName, cancellationToken);
            fileChars = Encoding.UTF8.GetChars(fileBytes);
            return DecodeShareKey(fileChars);
        }
        catch (IOException)
        {
            throw new DownloadCommandException("Share key invalid or missing.");
        }
        catch (UnauthorizedAccessException)
        {
            throw new DownloadCommandException("Share key invalid or missing.");
        }
        finally
        {
            if (fileBytes is not null)
            {
                CryptographicOperations.ZeroMemory(fileBytes);
            }

            if (fileChars is not null)
            {
                Array.Clear(fileChars, 0, fileChars.Length);
            }
        }
    }

    private ShareReference ResolveShareReference(String shareId, String? serverUrlOverride)
    {
        if (Uri.TryCreate(shareId, UriKind.Absolute, out var absoluteShareUri)
            && (absoluteShareUri.Scheme == Uri.UriSchemeHttp || absoluteShareUri.Scheme == Uri.UriSchemeHttps))
        {
            var segments = absoluteShareUri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length >= 2 && String.Equals(segments[^2], "d", StringComparison.OrdinalIgnoreCase))
            {
                var basePath = segments.Length == 2 ? "/" : $"/{String.Join('/', segments[..^2])}/";
                var builder = new UriBuilder(absoluteShareUri)
                {
                    Path = basePath,
                    Query = String.Empty,
                    Fragment = String.Empty
                };
                return new(builder.Uri, Uri.UnescapeDataString(segments[^1]));
            }

            throw new DownloadCommandException("Share id invalid or missing.");
        }

        var configuration = ResolveConfiguration(serverUrlOverride);
        if (!Uri.TryCreate(configuration.ServerUrl, UriKind.Absolute, out var serverUrl)
            || (serverUrl.Scheme != Uri.UriSchemeHttp && serverUrl.Scheme != Uri.UriSchemeHttps))
        {
            throw new DownloadCommandException("Server URL invalid or missing.");
        }

        return new(serverUrl, shareId);
    }

    private sealed record ShareReference(Uri ServerUrl, String ShareId);
}
