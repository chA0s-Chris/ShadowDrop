// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Uploads;

using ShadowDrop.Api.Configuration;

public sealed class S3BlobStorage : IBlobStorage
{
    internal const Int64 MaximumObjectSize = (Int64)MultipartPartSize * MaximumPartCount;
    internal const Int32 MaximumPartCount = 10_000;
    internal const Int32 MultipartPartSize = 8 * 1024 * 1024;

    private static readonly TimeSpan AbortTimeout = TimeSpan.FromSeconds(10);
    private readonly String _bucketName;
    private readonly IS3Client _client;
    private readonly String _keyPrefix;
    private readonly ILogger<S3BlobStorage> _logger;

    internal S3BlobStorage(ShadowDropOptions options, IS3Client client, ILogger<S3BlobStorage> logger)
    {
        _bucketName = options.Storage.S3.BucketName;
        _client = client;
        _keyPrefix = options.Storage.S3.KeyPrefix;
        _logger = logger;
    }

    private static async Task<(Int32 Count, Boolean ReachedEnd)> FillBufferAsync(Stream source, Byte[] buffer,
                                                                                 CancellationToken cancellationToken)
    {
        var count = 0;
        while (count < buffer.Length)
        {
            var bytesRead = await source.ReadAsync(buffer.AsMemory(count, buffer.Length - count), cancellationToken);
            if (bytesRead == 0)
            {
                return (count, true);
            }

            count += bytesRead;
        }

        return (count, false);
    }

    private static MemoryStream OpenBuffer(Byte[] buffer, Int32 count) => new(buffer, 0, count, false, true);

    private async Task<UploadBlobDescriptor> SaveMultipartAsync(String blobKey, String objectKey, Stream encryptedContent,
                                                                Byte[] buffer, CancellationToken cancellationToken)
    {
        String? uploadId = null;
        try
        {
            uploadId = await _client.InitiateMultipartUploadAsync(_bucketName, objectKey, cancellationToken);
            var parts = new List<S3UploadedPart>();
            Int64 writtenLength = 0;
            var partNumber = 1;
            var currentCount = buffer.Length;

            while (currentCount > 0)
            {
                if (partNumber > MaximumPartCount)
                {
                    throw new InvalidOperationException($"An S3 multipart upload cannot exceed {MaximumPartCount} parts.");
                }

                using (var content = OpenBuffer(buffer, currentCount))
                {
                    var eTag = await _client.UploadPartAsync(_bucketName, objectKey, uploadId, partNumber, content,
                                                             currentCount, cancellationToken);
                    parts.Add(new(partNumber, eTag));
                }

                writtenLength += currentCount;
                partNumber++;
                var next = await FillBufferAsync(encryptedContent, buffer, cancellationToken);
                currentCount = next.Count;
            }

            await _client.CompleteMultipartUploadAsync(_bucketName, objectKey, uploadId, parts, cancellationToken);
            uploadId = null;
            return new(blobKey, writtenLength);
        }
        catch
        {
            if (uploadId is not null)
            {
                try
                {
                    using var cleanup = new CancellationTokenSource(AbortTimeout);
                    await _client.AbortMultipartUploadAsync(_bucketName, objectKey, uploadId, cleanup.Token);
                }
                catch (Exception cleanupException)
                {
                    _logger.LogError(cleanupException,
                                     "S3 multipart upload abort failed; uploaded parts may remain. BlobKey: {BlobKey}",
                                     blobKey);
                }
            }

            throw;
        }
    }

    public async Task<Boolean> DeleteIfExistsAsync(String blobKey, CancellationToken cancellationToken)
    {
        var objectKey = S3ObjectKey.Build(blobKey, _keyPrefix);
        if (!await _client.DoesObjectExistAsync(_bucketName, objectKey, cancellationToken))
        {
            return false;
        }

        await _client.DeleteObjectAsync(_bucketName, objectKey, cancellationToken);
        return true;
    }

    public async Task<Stream> OpenReadAsync(String blobKey, CancellationToken cancellationToken)
    {
        var objectKey = S3ObjectKey.Build(blobKey, _keyPrefix);
        try
        {
            var length = await _client.GetObjectLengthAsync(_bucketName, objectKey, cancellationToken);
            return new S3SeekableReadStream(_client, _bucketName, objectKey, blobKey, length);
        }
        catch (S3ObjectNotFoundException exception)
        {
            throw new FileNotFoundException("The requested blob does not exist.", blobKey, exception);
        }
    }

    public async Task<UploadBlobDescriptor> SaveAsync(Guid fileId, Stream encryptedContent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(encryptedContent);

        var blobKey = fileId.ToString("N");
        var objectKey = S3ObjectKey.Build(fileId, _keyPrefix);
        var buffer = new Byte[MultipartPartSize];
        try
        {
            var first = await FillBufferAsync(encryptedContent, buffer, cancellationToken);
            UploadBlobDescriptor descriptor;
            if (first.ReachedEnd)
            {
                using var content = OpenBuffer(buffer, first.Count);
                await _client.PutObjectAsync(_bucketName, objectKey, content, first.Count, cancellationToken);
                descriptor = new(blobKey, first.Count);
            }
            else
            {
                descriptor = await SaveMultipartAsync(blobKey, objectKey, encryptedContent, buffer, cancellationToken);
            }

            _logger.LogDebug("S3 blob saved. BlobKey: {BlobKey}; Bytes: {Bytes}", blobKey, descriptor.WrittenLength);
            return descriptor;
        }
        catch (Exception exception)
        {
            _logger.Log(exception is UploadValidationException or UploadPayloadTooLargeException or OperationCanceledException
                            ? LogLevel.Debug
                            : LogLevel.Error,
                        exception,
                        "S3 blob save failed. BlobKey: {BlobKey}",
                        blobKey);
            throw;
        }
    }
}
