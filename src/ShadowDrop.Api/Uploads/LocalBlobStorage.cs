// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Uploads;

using ShadowDrop.Api.Configuration;
using ShadowDrop.Api.Infrastructure.Storage;

public sealed class LocalBlobStorage : IBlobStorage
{
    private readonly String _storageRoot;

    public LocalBlobStorage(ShadowDropOptions options)
    {
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
        return Task.FromResult(DeleteBlobFile(ResolveBlobPath(blobKey)));
    }

    public Task<Stream> OpenReadAsync(String blobKey, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(blobKey);
        cancellationToken.ThrowIfCancellationRequested();

        Stream stream = new FileStream(ResolveBlobPath(blobKey),
                                       FileMode.Open,
                                       FileAccess.Read,
                                       FileShare.Read,
                                       81_920,
                                       FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Task.FromResult(stream);
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

            return new(blobKey, writtenLength);
        }
        catch
        {
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
