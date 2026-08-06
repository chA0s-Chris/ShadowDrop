// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Uploads;

internal interface IS3Client
{
    Task AbortMultipartUploadAsync(String bucketName, String objectKey, String uploadId, CancellationToken cancellationToken);

    Task CheckBucketAsync(String bucketName, CancellationToken cancellationToken);

    Task CompleteMultipartUploadAsync(String bucketName, String objectKey, String uploadId,
                                      IReadOnlyCollection<S3UploadedPart> parts, CancellationToken cancellationToken);

    Task DeleteObjectAsync(String bucketName, String objectKey, CancellationToken cancellationToken);

    Task<Boolean> DoesObjectExistAsync(String bucketName, String objectKey, CancellationToken cancellationToken);

    Task<S3ReadResponse> GetObjectAsync(String bucketName, String objectKey, Int64 start, CancellationToken cancellationToken);

    Task<Int64> GetObjectLengthAsync(String bucketName, String objectKey, CancellationToken cancellationToken);

    Task<String> InitiateMultipartUploadAsync(String bucketName, String objectKey, CancellationToken cancellationToken);

    Task PutObjectAsync(String bucketName, String objectKey, Stream content, Int64 contentLength,
                        CancellationToken cancellationToken);

    Task<String> UploadPartAsync(String bucketName, String objectKey, String uploadId, Int32 partNumber,
                                 Stream content, Int64 contentLength, CancellationToken cancellationToken);
}

internal sealed record S3UploadedPart(Int32 PartNumber, String ETag);

internal sealed class S3ObjectNotFoundException : IOException
{
    public S3ObjectNotFoundException(Exception innerException)
        : base("The requested S3 object does not exist.", innerException) { }
}

internal sealed class S3ReadResponse : IDisposable
{
    private readonly IDisposable _response;

    public S3ReadResponse(Stream content, IDisposable response)
    {
        Content = content;
        _response = response;
    }

    public Stream Content { get; }

    public void Dispose() => _response.Dispose();
}
