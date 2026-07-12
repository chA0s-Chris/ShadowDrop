// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Uploads;

using Chaos.Mongo;
using MongoDB.Driver.GridFS;
using ShadowDrop.Api.Configuration;

public sealed class MongoGridFsBlobStorage : IBlobStorage
{
    private readonly GridFSBucket<Guid> _bucket;
    private readonly ILogger<MongoGridFsBlobStorage> _logger;

    public MongoGridFsBlobStorage(IMongoHelper mongo, ShadowDropOptions options, ILogger<MongoGridFsBlobStorage> logger)
    {
        _logger = logger;
        _bucket = new(mongo.Database, new()
        {
            BucketName = options.Storage.GridFsBucketName
        });
    }

    private static Guid ParseBlobKey(String blobKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(blobKey);
        if (blobKey.Length != 32 || !Guid.TryParseExact(blobKey, "N", out var fileId))
        {
            throw new ArgumentException("The GridFS blob key is malformed.", nameof(blobKey));
        }

        return fileId;
    }

    public async Task<Boolean> DeleteIfExistsAsync(String blobKey, CancellationToken cancellationToken)
    {
        var fileId = ParseBlobKey(blobKey);
        try
        {
            await _bucket.DeleteAsync(fileId, cancellationToken);
            return true;
        }
        catch (GridFSFileNotFoundException)
        {
            return false;
        }
    }

    public async Task<Stream> OpenReadAsync(String blobKey, CancellationToken cancellationToken)
    {
        var fileId = ParseBlobKey(blobKey);
        try
        {
            return await _bucket.OpenDownloadStreamAsync(fileId, new()
            {
                Seekable = true
            }, cancellationToken);
        }
        catch (GridFSFileNotFoundException exception)
        {
            throw new FileNotFoundException("The requested blob does not exist.", blobKey, exception);
        }
    }

    public async Task<UploadBlobDescriptor> SaveAsync(Guid fileId, Stream encryptedContent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(encryptedContent);
        var blobKey = fileId.ToString("N");
        GridFSUploadStream<Guid>? upload = null;
        try
        {
            upload = await _bucket.OpenUploadStreamAsync(fileId, blobKey, cancellationToken: cancellationToken);
            await using var countingStream = new CountingWriteStream(upload);
            await encryptedContent.CopyToAsync(countingStream, cancellationToken);
            await countingStream.FlushAsync(cancellationToken);
            await upload.CloseAsync(cancellationToken);
            return new(blobKey, countingStream.BytesWritten);
        }
        catch (Exception exception)
        {
            if (upload is not null)
            {
                try
                {
                    await upload.AbortAsync(CancellationToken.None);
                }
                catch (Exception abortException)
                {
                    _logger.LogError(abortException,
                                     "GridFS upload abort failed; chunk documents may remain. BlobKey: {BlobKey}",
                                     blobKey);
                }

                try
                {
                    _ = await DeleteIfExistsAsync(blobKey, CancellationToken.None);
                }
                catch (Exception cleanupException)
                {
                    _logger.LogError(cleanupException,
                                     "GridFS upload cleanup failed; file or chunk documents may remain. BlobKey: {BlobKey}",
                                     blobKey);
                }
            }

            _logger.Log(exception is OperationCanceledException ? LogLevel.Debug : LogLevel.Error,
                        exception,
                        "GridFS blob save failed. BlobKey: {BlobKey}",
                        blobKey);
            throw;
        }
        finally
        {
            if (upload is not null)
            {
                await upload.DisposeAsync();
            }
        }
    }
}
