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
    IDownloadProgressReporter progressReporter,
    CliBannerWriter bannerWriter)
{
    private const Int32 ResumeMarkerVersion = 1;
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

                // Direct downloads write raw decrypted bytes to stdout, so the banner always goes to stderr
                // alongside the progress reporter's own output, never to stdout.
                await bannerWriter.WriteToStandardErrorAsync(standardError, cancellationToken);
                var succeeded = await progressReporter.RunSingleAsync(
                    fileName,
                    file.Length,
                    (progress, token) => DownloadToStreamAsync(shareReference.ServerUrl, shareReference.ShareToken, file, shareKeyBytes, options.BearerToken,
                                                               standardOutStream, progress, token),
                    ClassifyDownloadError,
                    cancellationToken);
                return succeeded ? 0 : 1;
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
                                            IProgress<Int64>? progress = null,
                                            String? expectedPlaintextSha256 = null)
    {
        var directoryPath = Path.GetDirectoryName(outputPath);
        if (!String.IsNullOrWhiteSpace(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        var (partialPath, markerPath) = ResolvePartialPaths(outputPath);
        var marker = CreateResumeMarker(serverUrl, shareToken, file, expectedPlaintextSha256);

        if (await TrySkipCompletedOutputAsync(outputPath, marker, progress, cancellationToken))
        {
            ResetResumeState(partialPath, markerPath);
            return;
        }

        PrepareResumeState(partialPath, markerPath, marker);

        await using (var destination = new FileStream(partialPath,
                                                      FileMode.OpenOrCreate,
                                                      FileAccess.ReadWrite,
                                                      FileShare.None,
                                                      81920,
                                                      FileOptions.Asynchronous))
        {
            var durablePlaintextLength = destination.Length;
            destination.Position = durablePlaintextLength;
            // Only write the marker when it is missing or stale. Rewriting a matching marker on every resume would
            // needlessly truncate a valid file, and a crash mid-write would orphan the partial on the next run.
            if (!File.Exists(markerPath) || LoadResumeMarker(markerPath) is not { } persistedMarker || !IsResumeMarkerMatch(persistedMarker, marker))
            {
                await PersistResumeMarkerAsync(markerPath, marker, cancellationToken);
            }

            await DownloadToStreamAsync(serverUrl,
                                        shareToken,
                                        file,
                                        shareKeyBytes,
                                        bearerToken,
                                        destination,
                                        progress,
                                        cancellationToken,
                                        durablePlaintextLength,
                                        file.Length);
            await destination.FlushAsync(cancellationToken);
        }

        File.Move(partialPath, outputPath, true);
        File.Delete(markerPath);
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

    // Shared by the queue loop and the single-file path: returns a user-facing message for handled failures, otherwise null to rethrow.
    private static String? ClassifyDownloadError(Exception exception) => exception switch
    {
        DownloadCommandException downloadCommandException => downloadCommandException.Message,
        IOException => "Download failed due to a local I/O error.",
        UnauthorizedAccessException => "Download failed due to a local I/O error.",
        _ => null
    };

    private static DownloadResumeMarker CreateResumeMarker(Uri serverUrl,
                                                           String shareToken,
                                                           ShareManifestFileContract file,
                                                           String? expectedPlaintextSha256)
    {
        var fileId = ParseFileId(file.FileId);
        return new(ResumeMarkerVersion,
                   serverUrl.AbsoluteUri,
                   shareToken,
                   fileId.ToString("D"),
                   file.FileName,
                   file.Length,
                   file.KdfSalt,
                   expectedPlaintextSha256 ?? file.PlaintextSha256);
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

    private static Boolean HasExplicitCredentials(DownloadCommandOptions options) =>
        !String.IsNullOrWhiteSpace(options.ShareKey) || options.ShareKeyFile is not null || !String.IsNullOrWhiteSpace(options.BearerToken);

    private static async Task<String> HashFileSha256Async(String path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexStringLower(hash);
    }

    private static async Task<Boolean> IsExistingOutputMatchAsync(String outputPath,
                                                                  Int64 expectedLength,
                                                                  String? expectedPlaintextSha256,
                                                                  CancellationToken cancellationToken)
    {
        var fileInfo = new FileInfo(outputPath);
        if (!fileInfo.Exists || fileInfo.Length != expectedLength)
        {
            return false;
        }

        return expectedPlaintextSha256 is null ||
               String.Equals(await HashFileSha256Async(outputPath, cancellationToken), expectedPlaintextSha256, StringComparison.Ordinal);
    }

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

    private static Boolean IsResumeMarkerMatch(DownloadResumeMarker marker, DownloadResumeMarker expected) =>
        marker.Version == expected.Version &&
        marker.FileLength == expected.FileLength &&
        String.Equals(marker.ServerUrl, expected.ServerUrl, StringComparison.Ordinal) &&
        String.Equals(marker.ShareToken, expected.ShareToken, StringComparison.Ordinal) &&
        String.Equals(marker.FileId, expected.FileId, StringComparison.Ordinal) &&
        String.Equals(marker.FileName, expected.FileName, StringComparison.Ordinal) &&
        String.Equals(marker.KdfSalt, expected.KdfSalt, StringComparison.Ordinal) &&
        String.Equals(marker.PlaintextSha256, expected.PlaintextSha256, StringComparison.Ordinal);

    private static DownloadResumeMarker? LoadResumeMarker(String markerPath)
    {
        try
        {
            using var stream = File.OpenRead(markerPath);
            return JsonSerializer.Deserialize(stream, CliJsonSerializerContext.Default.DownloadResumeMarker);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static async Task PersistResumeMarkerAsync(String markerPath, DownloadResumeMarker marker, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(markerPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous);
        await JsonSerializer.SerializeAsync(stream, marker, CliJsonSerializerContext.Default.DownloadResumeMarker, cancellationToken);
    }

    private static void PrepareResumeState(String partialPath, String markerPath, DownloadResumeMarker expectedMarker)
    {
        var partialExists = File.Exists(partialPath);
        var markerExists = File.Exists(markerPath);

        if (!partialExists)
        {
            if (markerExists)
            {
                File.Delete(markerPath);
            }

            return;
        }

        var partialLength = new FileInfo(partialPath).Length;
        if (partialLength == 0)
        {
            var marker = markerExists ? LoadResumeMarker(markerPath) : null;
            if (markerExists && (marker is null || !IsResumeMarkerMatch(marker, expectedMarker)))
            {
                File.Delete(markerPath);
            }

            return;
        }

        var canResume = partialLength <= expectedMarker.FileLength &&
                        markerExists &&
                        LoadResumeMarker(markerPath) is { } resumeMarker &&
                        IsResumeMarkerMatch(resumeMarker, expectedMarker);
        if (!canResume)
        {
            ResetResumeState(partialPath, markerPath);
        }
    }

    private static void ResetResumeState(String partialPath, String markerPath)
    {
        if (File.Exists(partialPath))
        {
            File.Delete(partialPath);
        }

        if (File.Exists(markerPath))
        {
            File.Delete(markerPath);
        }
    }

    private static String? ResolveExpectedPlaintextSha256(QueueFileEntry entry, ShareManifestFileContract file)
    {
        if (entry.PlaintextSha256 is not null &&
            file.PlaintextSha256 is not null &&
            !String.Equals(entry.PlaintextSha256, file.PlaintextSha256, StringComparison.Ordinal))
        {
            throw new DownloadCommandException("Queue entry does not match share metadata.");
        }

        return entry.PlaintextSha256 ?? file.PlaintextSha256;
    }

    private static String ResolveOutputRoot(DirectoryInfo? outputRoot)
    {
        var root = outputRoot?.FullName ?? Environment.CurrentDirectory;
        return Path.GetFullPath(root);
    }

    private static (String PartialPath, String MarkerPath) ResolvePartialPaths(String outputPath)
    {
        var partialPath = $"{outputPath}.shadowdrop-partial";
        return (partialPath, $"{partialPath}.json");
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
    // An overflow is treated the same way (total unknown): the queue total only feeds the optional ETA, so it must degrade
    // gracefully rather than abort the download.
    private static Int64? SumQueueBytes(IReadOnlyList<QueueFileEntry> entries)
    {
        try
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
        catch (OverflowException)
        {
            return null;
        }
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

    private static async Task<Boolean> TrySkipCompletedOutputAsync(String outputPath,
                                                                   DownloadResumeMarker marker,
                                                                   IProgress<Int64>? progress,
                                                                   CancellationToken cancellationToken)
    {
        if (!File.Exists(outputPath))
        {
            return false;
        }

        if (!await IsExistingOutputMatchAsync(outputPath, marker.FileLength, marker.PlaintextSha256, cancellationToken))
        {
            throw new DownloadCommandException("Existing output file does not match the shared file.");
        }

        progress?.Report(marker.FileLength);
        return true;
    }

    private static async Task<Boolean> TrySkipCompletedQueueOutputAsync(QueueFileEntry entry,
                                                                        String outputPath,
                                                                        IProgress<Int64>? progress,
                                                                        CancellationToken cancellationToken)
    {
        if (!File.Exists(outputPath))
        {
            return false;
        }

        var expectedLength = entry.Length!.Value;
        if (!await IsExistingOutputMatchAsync(outputPath, expectedLength, entry.PlaintextSha256, cancellationToken))
        {
            throw new DownloadCommandException("Existing output file does not match the queue entry.");
        }

        var (partialPath, markerPath) = ResolvePartialPaths(outputPath);
        ResetResumeState(partialPath, markerPath);
        progress?.Report(expectedLength);
        return true;
    }

    private async Task DownloadToStreamAsync(Uri serverUrl,
                                             String shareToken,
                                             ShareManifestFileContract file,
                                             Byte[] shareKeyBytes,
                                             String? bearerToken,
                                             Stream destination,
                                             IProgress<Int64>? progress,
                                             CancellationToken cancellationToken,
                                             Int64 durablePlaintextLength = 0,
                                             Int64? totalPlaintextSize = null)
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
                                                       durablePlaintextLength,
                                                       totalPlaintextSize,
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
                                                    if (await TrySkipCompletedQueueOutputAsync(entry, outputPath, progress, token))
                                                    {
                                                        return;
                                                    }

                                                    var shareReference = ResolveQueueShareReference(entry);
                                                    var manifest = await GetManifestAsync(manifestClient, manifestCache, shareReference, bearerToken, token);
                                                    var file = SelectQueuedFile(manifest, entry);
                                                    await DownloadToFileAsync(shareReference.ServerUrl,
                                                                              shareReference.ShareToken,
                                                                              file,
                                                                              capturedShareKeyBytes,
                                                                              bearerToken,
                                                                              outputPath,
                                                                              token,
                                                                              progress,
                                                                              ResolveExpectedPlaintextSha256(entry, file));
                                                }))
                             .ToList();

            var totalBytes = SumQueueBytes(queue.Files!);

            // Queue downloads write decrypted files to disk, not stdout, but the banner still goes to stderr
            // alongside the progress reporter's own output for consistency with the direct-download path.
            await bannerWriter.WriteToStandardErrorAsync(standardError, cancellationToken);
            var summary = await progressReporter.RunQueueAsync(items, totalBytes, ClassifyDownloadError, cancellationToken);
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
