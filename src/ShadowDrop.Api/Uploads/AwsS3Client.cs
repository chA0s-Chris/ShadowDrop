// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Uploads;

using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using ShadowDrop.Api.Configuration;
using System.Net;

internal sealed class AwsS3Client : IS3Client, IDisposable
{
    private readonly IAmazonS3 _client;

    public AwsS3Client(ShadowDropOptions options)
    {
        _client = CreateClient(options.Storage.S3);
    }

    private static IAmazonS3 CreateClient(S3StorageOptions options)
    {
        var configuration = new AmazonS3Config
        {
            ForcePathStyle = options.UsePathStyle
        };

        if (String.IsNullOrWhiteSpace(options.ServiceEndpoint))
        {
            configuration.RegionEndpoint = RegionEndpoint.GetBySystemName(options.Region);
        }
        else
        {
            configuration.ServiceURL = options.ServiceEndpoint;
            configuration.AuthenticationRegion = options.Region;
        }

        if (!String.IsNullOrWhiteSpace(options.AccessKeyId))
        {
            AWSCredentials credentials = new BasicAWSCredentials(options.AccessKeyId, options.SecretAccessKey);
            return new AmazonS3Client(credentials, configuration);
        }

        return new AmazonS3Client(configuration);
    }

    private static Boolean IsNotFound(AmazonS3Exception exception) =>
        exception.StatusCode == HttpStatusCode.NotFound
        || String.Equals(exception.ErrorCode, "NoSuchKey", StringComparison.Ordinal)
        || String.Equals(exception.ErrorCode, "NotFound", StringComparison.Ordinal);

    public void Dispose() => _client.Dispose();

    public Task AbortMultipartUploadAsync(String bucketName, String objectKey, String uploadId,
                                          CancellationToken cancellationToken) =>
        _client.AbortMultipartUploadAsync(new()
        {
            BucketName = bucketName,
            Key = objectKey,
            UploadId = uploadId
        }, cancellationToken);

    public async Task CheckBucketAsync(String bucketName, CancellationToken cancellationToken)
    {
        _ = await _client.ListObjectsV2Async(new()
        {
            BucketName = bucketName,
            MaxKeys = 0
        }, cancellationToken);
    }

    public Task CompleteMultipartUploadAsync(String bucketName, String objectKey, String uploadId,
                                             IReadOnlyCollection<S3UploadedPart> parts,
                                             CancellationToken cancellationToken) =>
        _client.CompleteMultipartUploadAsync(new()
        {
            BucketName = bucketName,
            Key = objectKey,
            UploadId = uploadId,
            PartETags = parts.Select(static part => new PartETag(part.PartNumber, part.ETag)).ToList()
        }, cancellationToken);

    public Task DeleteObjectAsync(String bucketName, String objectKey, CancellationToken cancellationToken) =>
        _client.DeleteObjectAsync(bucketName, objectKey, cancellationToken);

    public async Task<Boolean> DoesObjectExistAsync(String bucketName, String objectKey, CancellationToken cancellationToken)
    {
        try
        {
            _ = await _client.GetObjectMetadataAsync(bucketName, objectKey, cancellationToken);
            return true;
        }
        catch (AmazonS3Exception exception) when (IsNotFound(exception))
        {
            return false;
        }
    }

    public async Task<S3ReadResponse> GetObjectAsync(String bucketName, String objectKey, Int64 start,
                                                     CancellationToken cancellationToken)
    {
        try
        {
            var response = await _client.GetObjectAsync(new()
            {
                BucketName = bucketName,
                Key = objectKey,
                ByteRange = new($"bytes={start}-")
            }, cancellationToken);
            return new(response.ResponseStream, response);
        }
        catch (AmazonS3Exception exception) when (IsNotFound(exception))
        {
            throw new S3ObjectNotFoundException(exception);
        }
    }

    public async Task<Int64> GetObjectLengthAsync(String bucketName, String objectKey, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _client.GetObjectMetadataAsync(bucketName, objectKey, cancellationToken);
            return response.ContentLength;
        }
        catch (AmazonS3Exception exception) when (IsNotFound(exception))
        {
            throw new S3ObjectNotFoundException(exception);
        }
    }

    public async Task<String> InitiateMultipartUploadAsync(String bucketName, String objectKey,
                                                           CancellationToken cancellationToken)
    {
        var response = await _client.InitiateMultipartUploadAsync(bucketName, objectKey, cancellationToken);
        return response.UploadId;
    }

    public async Task PutObjectAsync(String bucketName, String objectKey, Stream content, Int64 contentLength,
                                     CancellationToken cancellationToken)
    {
        var request = new PutObjectRequest
        {
            BucketName = bucketName,
            Key = objectKey,
            InputStream = content,
            AutoCloseStream = false,
            Headers =
            {
                ContentLength = contentLength
            }
        };
        _ = await _client.PutObjectAsync(request, cancellationToken);
    }

    public async Task<String> UploadPartAsync(String bucketName, String objectKey, String uploadId, Int32 partNumber,
                                              Stream content, Int64 contentLength, CancellationToken cancellationToken)
    {
        var response = await _client.UploadPartAsync(new()
        {
            BucketName = bucketName,
            Key = objectKey,
            UploadId = uploadId,
            PartNumber = partNumber,
            InputStream = content,
            PartSize = contentLength
        }, cancellationToken);
        return response.ETag;
    }
}
