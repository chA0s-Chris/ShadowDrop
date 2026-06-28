// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Downloads;

using ShadowDrop.Cli.Configuration;
using ShadowDrop.Cli.Downloads.Progress;
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
    TextWriter standardError,
    IDownloadProgressReporter progressReporter)
{
    private const String SecretFreeQueueKeyMissingMessage =
        "The queue is secret-free and contains no credentials. Provide the share key with --share-key or --share-key-file. "
        + "The key is printed by 'upload' as 'share-key:', or stored as the 'shareKey' value in its --secrets-out file. "
        + "Alternatively, recreate the queue with --embed-secrets.";
    private const String SecretPrefix = "secret:";

    public async Task<Int32> ExecuteAsync(DownloadCommandOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);

        try
        {
            if (options.QueuePath is not null)
            {
                if (!String.IsNullOrWhiteSpace(options.ShareToken)
                    || !String.IsNullOrWhiteSpace(options.FileId)
                    || !String.IsNullOrWhiteSpace(options.ServerUrlOverride))
                {
                    await standardError.WriteLineAsync("The --queue option cannot be combined with a share token, --file, or --server-url.");
                    return 1;
                }

                return await ExecuteQueueAsync(options.QueuePath, options, cancellationToken);
            }

            var shareKeyBytes = await ResolveShareKeyBytesAsync(options, cancellationToken);
            if (shareKeyBytes is null)
            {
                await standardError.WriteLineAsync("Share key invalid or missing.");
                return 1;
            }

            try
            {
                if (String.IsNullOrWhiteSpace(options.ShareToken))
                {
                    await standardError.WriteLineAsync("Specify either a share token or --queue.");
                    return 1;
                }

                var shareReference = ResolveShareReference(options.ShareToken, options.ServerUrlOverride);
                var manifestClient = new ShareManifestClient(httpClient);
                var manifest = await manifestClient.GetAsync(shareReference.ServerUrl, shareReference.ShareToken, options.BearerToken, cancellationToken);
                var file = SelectDirectDownloadFile(manifest, options.FileId);
                var fileName = file.FileName ?? file.FileId ?? "download.bin";
                await progressReporter.RunSingleAsync(
                    fileName,
                    file.Length,
                    (progress, token) => DownloadToStreamAsync(shareReference.ServerUrl, shareReference.ShareToken, file, shareKeyBytes, options.BearerToken,
                                                               standardOutStream, progress, token),
                    cancellationToken);
                return 0;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(shareKeyBytes);
            }
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
    }

    internal static Byte[] DecodeShareKey(String shareKey) => DecodeShareKey(shareKey.AsSpan());

    internal static Byte[] DecodeShareKey(ReadOnlySpan<Char> shareKey)
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

    internal static Guid ParseFileId(String? fileId)
    {
        if (!Guid.TryParse(fileId, out var parsedFileId) || parsedFileId == Guid.Empty)
        {
            throw new DownloadCommandException("Share metadata invalid or missing.");
        }

        return parsedFileId;
    }

    internal static ShareManifestFileContract SelectDirectDownloadFile(ShareManifestContract manifest, String? fileId)
    {
        if (!String.IsNullOrWhiteSpace(fileId))
        {
            return SelectFileById(manifest.Files!, ParseFileId(fileId));
        }

        return manifest.Files!.Count == 1
            ? manifest.Files[0]
            : throw new DownloadCommandException("Share contains multiple files; specify --file.");
    }

    internal static ShareManifestFileContract SelectFileById(IReadOnlyList<ShareManifestFileContract> files, Guid fileId)
    {
        ShareManifestFileContract? match = null;
        foreach (var file in files)
        {
            if (ParseFileId(file.FileId) != fileId)
            {
                continue;
            }

            if (match is not null)
            {
                throw new DownloadCommandException("Share metadata invalid or missing.");
            }

            match = file;
        }

        return match ?? throw new DownloadCommandException("Requested file not found in share.");
    }

    internal async Task DownloadToFileAsync(Uri serverUrl,
                                            String shareToken,
                                            ShareManifestFileContract file,
                                            Byte[] shareKeyBytes,
                                            String? bearerToken,
                                            String outputPath,
                                            CancellationToken cancellationToken,
                                            IProgress<Int64>? progress = null)
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
            await DownloadToStreamAsync(serverUrl, shareToken, file, shareKeyBytes, bearerToken, destination, progress, cancellationToken);
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

    internal async Task<ShareManifestContract> GetManifestAsync(Uri serverUrl, String shareToken, String? bearerToken, CancellationToken cancellationToken)
    {
        var manifestClient = new ShareManifestClient(httpClient);
        return await manifestClient.GetAsync(serverUrl, shareToken, bearerToken, cancellationToken);
    }

    internal async Task<Byte[]?> ResolveShareKeyBytesAsync(DownloadCommandOptions options, CancellationToken cancellationToken)
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

    // Mirrors the per-entry exception handling of the queue loop: returns a user-facing message for handled failures, otherwise null to rethrow.
    private static String? ClassifyQueueError(Exception exception) => exception switch
    {
        DownloadCommandException downloadCommandException => downloadCommandException.Message,
        IOException => "Download failed due to a local I/O error.",
        UnauthorizedAccessException => "Download failed due to a local I/O error.",
        _ => null
    };

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

    private static Boolean HasExplicitCredentials(DownloadCommandOptions options) =>
        !String.IsNullOrWhiteSpace(options.ShareKey) || options.ShareKeyFile is not null || !String.IsNullOrWhiteSpace(options.BearerToken);

    private static Boolean IsInsideRoot(String path, String root)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (String.Equals(path, root, comparison))
        {
            return true;
        }

        var rootPrefix = root.EndsWith(Path.DirectorySeparatorChar) || root.EndsWith(Path.AltDirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        return path.StartsWith(rootPrefix, comparison);
    }

    private static String ResolveOutputRoot(DirectoryInfo? outputRoot)
    {
        var root = outputRoot?.FullName ?? Environment.CurrentDirectory;
        return Path.GetFullPath(root);
    }

    private static String ResolveQueueOutputPath(String outputRoot, String outputPath)
    {
        var resolvedPath = Path.GetFullPath(Path.Combine(outputRoot, outputPath));
        if (!IsInsideRoot(resolvedPath, outputRoot))
        {
            throw new DownloadCommandException("Queue output path escapes the output root.");
        }

        return resolvedPath;
    }

    private static ShareReference ResolveQueueShareReference(QueueFileEntry entry)
    {
        if (!Uri.TryCreate(entry.ServerUrl, UriKind.Absolute, out var serverUrl)
            || (serverUrl.Scheme != Uri.UriSchemeHttp && serverUrl.Scheme != Uri.UriSchemeHttps))
        {
            throw new DownloadCommandException("Server URL invalid or missing.");
        }

        return new(serverUrl, entry.ShareToken!);
    }

    private static ShareManifestFileContract SelectQueuedFile(ShareManifestContract manifest, QueueFileEntry entry)
    {
        var match = SelectFileById(manifest.Files!, ParseFileId(entry.FileId));
        if (!String.Equals(match.FileName, entry.FileName, StringComparison.Ordinal) || match.Length != entry.Length)
        {
            throw new DownloadCommandException("Queue entry does not match share metadata.");
        }

        return match;
    }

    // The whole-queue size is only meaningful when every entry declares its length; otherwise the queue-level ETA is suppressed.
    private static Int64? SumQueueBytes(IReadOnlyList<QueueFileEntry> entries)
    {
        Int64 total = 0;
        foreach (var entry in entries)
        {
            if (entry.Length is not { } length)
            {
                return null;
            }

            total = checked(total + length);
        }

        return total;
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

    private async Task DownloadToStreamAsync(Uri serverUrl,
                                             String shareToken,
                                             ShareManifestFileContract file,
                                             Byte[] shareKeyBytes,
                                             String? bearerToken,
                                             Stream destination,
                                             IProgress<Int64>? progress,
                                             CancellationToken cancellationToken)
    {
        var fileId = ParseFileId(file.FileId);
        var downloadUri = ShareDownloadUriFactory.CreateFileUri(serverUrl, shareToken, fileId);
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
                                                       bearerToken,
                                                       progress: progress);
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

    private async Task<Int32> ExecuteQueueAsync(FileInfo queuePath, DownloadCommandOptions options, CancellationToken cancellationToken)
    {
        var queue = await LoadQueueAsync(queuePath, cancellationToken);

        Byte[]? shareKeyBytes = null;
        try
        {
            String? bearerToken;
            if (queue.Credentials is not null)
            {
                // A self-contained queue carries its own credentials; mixing them with explicit inputs is ambiguous.
                if (HasExplicitCredentials(options))
                {
                    await standardError.WriteLineAsync(
                        "The queue already contains credentials; do not also pass --share-key, --share-key-file, or --bearer-token.");
                    return 1;
                }

                shareKeyBytes = DecodeShareKey(queue.Credentials.ShareKey!);
                bearerToken = queue.Credentials.DownloadBearerToken;
            }
            else
            {
                shareKeyBytes = await ResolveShareKeyBytesAsync(options, cancellationToken);
                if (shareKeyBytes is null)
                {
                    await standardError.WriteLineAsync(SecretFreeQueueKeyMissingMessage);
                    return 1;
                }

                bearerToken = options.BearerToken;
            }

            var manifestClient = new ShareManifestClient(httpClient);
            Dictionary<String, ShareManifestContract> manifestCache = [];
            var outputRoot = ResolveOutputRoot(options.OutputRoot);
            var capturedShareKeyBytes = shareKeyBytes;

            var items = queue.Files!.Select(entry => new QueueDownloadItem(
                                                entry.FileName ?? entry.FileId ?? "unknown",
                                                entry.Length,
                                                entry.OutputPath!,
                                                async (progress, token) =>
                                                {
                                                    var outputPath = ResolveQueueOutputPath(outputRoot, entry.OutputPath!);
                                                    var shareReference = ResolveQueueShareReference(entry);
                                                    var manifest = await GetManifestAsync(manifestClient, manifestCache, shareReference, bearerToken, token);
                                                    var file = SelectQueuedFile(manifest, entry);
                                                    await DownloadToFileAsync(shareReference.ServerUrl, shareReference.ShareToken, file,
                                                                              capturedShareKeyBytes, bearerToken, outputPath, token, progress);
                                                }))
                             .ToList();

            var totalBytes = SumQueueBytes(queue.Files!);
            var summary = await progressReporter.RunQueueAsync(items, totalBytes, ClassifyQueueError, cancellationToken);
            return summary.Failed == 0 ? 0 : 1;
        }
        finally
        {
            if (shareKeyBytes is not null)
            {
                CryptographicOperations.ZeroMemory(shareKeyBytes);
            }
        }
    }

    private async Task<ShareManifestContract> GetManifestAsync(ShareManifestClient manifestClient,
                                                               IDictionary<String, ShareManifestContract> manifestCache,
                                                               ShareReference shareReference,
                                                               String? bearerToken,
                                                               CancellationToken cancellationToken)
    {
        var cacheKey = ShareDownloadUriFactory.CreateManifestUri(shareReference.ServerUrl, shareReference.ShareToken).AbsoluteUri;
        if (manifestCache.TryGetValue(cacheKey, out var cachedManifest))
        {
            return cachedManifest;
        }

        var manifest = await manifestClient.GetAsync(shareReference.ServerUrl, shareReference.ShareToken, bearerToken, cancellationToken);
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

    private ShareReference ResolveShareReference(String shareToken, String? serverUrlOverride)
    {
        if (!ShareReferenceResolver.TryResolve(shareToken, null, out var resolvedServerUrl, out var resolvedToken))
        {
            throw new DownloadCommandException("Share token invalid or missing.");
        }

        // A full share URL carries its own server; a bare token is paired with the configured server.
        if (resolvedServerUrl is not null)
        {
            return new(resolvedServerUrl, resolvedToken);
        }

        var configuration = ResolveConfiguration(serverUrlOverride);
        if (!Uri.TryCreate(configuration.ServerUrl, UriKind.Absolute, out var serverUrl)
            || (serverUrl.Scheme != Uri.UriSchemeHttp && serverUrl.Scheme != Uri.UriSchemeHttps))
        {
            throw new DownloadCommandException("Server URL invalid or missing.");
        }

        return new(serverUrl, resolvedToken);
    }

    private sealed record ShareReference(Uri ServerUrl, String ShareToken);
}
