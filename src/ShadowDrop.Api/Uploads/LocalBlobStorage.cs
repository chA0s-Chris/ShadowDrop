// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Uploads;

using ShadowDrop.Api.Configuration;
using ShadowDrop.Api.Infrastructure.Storage;

public sealed class LocalBlobStorage : IBlobStorage
{
    private readonly ILogger<LocalBlobStorage> _logger;
    private readonly String _storageRoot;

    public LocalBlobStorage(ShadowDropOptions options, ILogger<LocalBlobStorage> logger)
    {
        _logger = logger;
        _storageRoot = options.Storage.LocalRoot;
        FileSystemAccessPermissions.EnsureOwnerOnlyDirectory(_storageRoot);
    }

    private static String BuildBlobKey(Guid fileId)
    {
        var opaqueId = fileId.ToString("N");
        return Path.Combine(opaqueId[..2], $"{opaqueId}.blob");
    }

    private Boolean DeleteBlobFile(String blobPath)
    {
        if (!File.Exists(blobPath))
        {
            return false;
        }

        File.Delete(blobPath);

        var directory = Path.GetDirectoryName(blobPath);
        while (!String.IsNullOrEmpty(directory)
               && !String.Equals(directory, _storageRoot, StringComparison.Ordinal)
               && Directory.Exists(directory)
               && !Directory.EnumerateFileSystemEntries(directory).Any())
        {
            Directory.Delete(directory);
            directory = Path.GetDirectoryName(directory);
        }

        return true;
    }

    private String ResolveBlobPath(String blobKey) => Path.Combine(_storageRoot, blobKey);

    public Task<Boolean> DeleteIfExistsAsync(String blobKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var deleted = DeleteBlobFile(ResolveBlobPath(blobKey));
        _logger.LogDebug("Blob delete completed. BlobKey: {BlobKey}; Deleted: {Deleted}", blobKey, deleted);
        return Task.FromResult(deleted);
    }

    public Task<Stream> OpenReadAsync(String blobKey, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(blobKey);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            Stream stream = new FileStream(ResolveBlobPath(blobKey),
                                           FileMode.Open,
                                           FileAccess.Read,
                                           FileShare.Read,
                                           81_920,
                                           FileOptions.Asynchronous | FileOptions.SequentialScan);
            _logger.LogDebug("Blob opened for read. BlobKey: {BlobKey}", blobKey);
            return Task.FromResult(stream);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            _logger.LogDebug("Blob open failed because the file was missing. BlobKey: {BlobKey}", blobKey);
            throw;
        }
    }

    public async Task<UploadBlobDescriptor> SaveAsync(Guid fileId, Stream encryptedContent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(encryptedContent);

        var blobKey = BuildBlobKey(fileId);
        var blobPath = ResolveBlobPath(blobKey);
        var blobDirectory = Path.GetDirectoryName(blobPath)
                            ?? throw new InvalidOperationException("The blob path must include a directory.");

        FileSystemAccessPermissions.EnsureOwnerOnlyDirectory(blobDirectory);

        var blobCreated = false;
        try
        {
            Int64 writtenLength;
            await using (var blobStream = new FileStream(blobPath,
                                                         FileMode.CreateNew,
                                                         FileAccess.Write,
                                                         FileShare.None,
                                                         81_920,
                                                         FileOptions.Asynchronous))
            {
                blobCreated = true;
                await encryptedContent.CopyToAsync(blobStream, cancellationToken);
                await blobStream.FlushAsync(cancellationToken);
                writtenLength = blobStream.Length;
            }

            FileSystemAccessPermissions.EnsureOwnerOnlyFile(blobPath);

            _logger.LogDebug("Blob saved. BlobKey: {BlobKey}; Bytes: {Bytes}", blobKey, writtenLength);
            return new(blobKey, writtenLength);
        }
        catch (Exception exception)
        {
            // Client-caused aborts (oversized payload, declared-length mismatch, disconnect) surface here because the
            // request body is read while copying. They are routine outcomes, not storage failures.
            if (exception is UploadValidationException or UploadPayloadTooLargeException or OperationCanceledException)
            {
                _logger.LogDebug("Blob save aborted. BlobKey: {BlobKey}", blobKey);
            }
            else
            {
                _logger.LogError(exception, "Blob save failed. BlobKey: {BlobKey}", blobKey);
            }

            try
            {
                if (blobCreated && File.Exists(blobPath))
                {
                    DeleteBlobFile(blobPath);
                }
            }
            catch
            {
                // best-effort cleanup after a failed write
            }

            throw;
        }
    }
}
