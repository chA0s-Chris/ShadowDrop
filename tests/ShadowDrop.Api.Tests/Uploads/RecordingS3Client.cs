// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Uploads;

using ShadowDrop.Api.Uploads;

internal sealed class RecordingS3Client : IS3Client
{
    private readonly Dictionary<String, List<Byte[]>> _multipartUploads = [];
    private readonly Dictionary<String, Byte[]> _objects = [];

    public Int32 AbortCount { get; private set; }

    public Func<CancellationToken, Task>? CheckBucket { get; set; }

    public Int32? FailingPartNumber { get; set; }

    public IReadOnlyDictionary<String, Byte[]> Objects => _objects;

    public Exception PartFailure { get; set; } = new IOException("injected part failure");

    public Int32 PutObjectCount { get; private set; }

    public List<Int64> RangeStarts { get; } = [];

    public Boolean ReadAsMissing { get; set; }

    public void SetObject(String bucketName, String objectKey, Byte[] content) =>
        _objects[ObjectId(bucketName, objectKey)] = content;

    private static String ObjectId(String bucketName, String objectKey) => $"{bucketName}/{objectKey}";

    private static async Task<Byte[]> ReadAllAsync(Stream content, CancellationToken cancellationToken)
    {
        using var copy = new MemoryStream();
        await content.CopyToAsync(copy, cancellationToken);
        return copy.ToArray();
    }

    public Task AbortMultipartUploadAsync(String bucketName, String objectKey, String uploadId,
                                          CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AbortCount++;
        _multipartUploads.Remove(uploadId);
        return Task.CompletedTask;
    }

    public Task CheckBucketAsync(String bucketName, CancellationToken cancellationToken) =>
        CheckBucket?.Invoke(cancellationToken) ?? Task.CompletedTask;

    public Task CompleteMultipartUploadAsync(String bucketName, String objectKey, String uploadId,
                                             IReadOnlyCollection<S3UploadedPart> parts,
                                             CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var content = _multipartUploads[uploadId].SelectMany(static part => part).ToArray();
        _objects[ObjectId(bucketName, objectKey)] = content;
        _multipartUploads.Remove(uploadId);
        return Task.CompletedTask;
    }

    public Task DeleteObjectAsync(String bucketName, String objectKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _objects.Remove(ObjectId(bucketName, objectKey));
        return Task.CompletedTask;
    }

    public Task<Boolean> DoesObjectExistAsync(String bucketName, String objectKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_objects.ContainsKey(ObjectId(bucketName, objectKey)));
    }

    public Task<S3ReadResponse> GetObjectAsync(String bucketName, String objectKey, Int64 start,
                                               CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (ReadAsMissing)
        {
            throw new S3ObjectNotFoundException(new IOException("missing"));
        }

        var content = _objects[ObjectId(bucketName, objectKey)];
        RangeStarts.Add(start);
        var stream = new MemoryStream(content[(Int32)start..], false);
        return Task.FromResult(new S3ReadResponse(stream, stream));
    }

    public Task<Int64> GetObjectLengthAsync(String bucketName, String objectKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_objects.TryGetValue(ObjectId(bucketName, objectKey), out var content))
        {
            throw new S3ObjectNotFoundException(new IOException("missing"));
        }

        return Task.FromResult((Int64)content.Length);
    }

    public Task<String> InitiateMultipartUploadAsync(String bucketName, String objectKey,
                                                     CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var uploadId = Guid.NewGuid().ToString("N");
        _multipartUploads.Add(uploadId, []);
        return Task.FromResult(uploadId);
    }

    public async Task PutObjectAsync(String bucketName, String objectKey, Stream content, Int64 contentLength,
                                     CancellationToken cancellationToken)
    {
        PutObjectCount++;
        var bytes = await ReadAllAsync(content, cancellationToken);
        if (bytes.LongLength != contentLength)
        {
            throw new InvalidDataException("The supplied content length did not match the stream.");
        }

        _objects[ObjectId(bucketName, objectKey)] = bytes;
    }

    public async Task<String> UploadPartAsync(String bucketName, String objectKey, String uploadId, Int32 partNumber,
                                              Stream content, Int64 contentLength, CancellationToken cancellationToken)
    {
        if (partNumber == FailingPartNumber)
        {
            throw PartFailure;
        }

        var bytes = await ReadAllAsync(content, cancellationToken);
        if (bytes.LongLength != contentLength)
        {
            throw new InvalidDataException("The supplied part length did not match the stream.");
        }

        _multipartUploads[uploadId].Add(bytes);
        return $"etag-{partNumber}";
    }
}
