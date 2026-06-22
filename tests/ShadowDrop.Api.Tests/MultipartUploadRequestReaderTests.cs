// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests;

using FluentAssertions;
using Microsoft.AspNetCore.Http;
using NUnit.Framework;
using ShadowDrop.Api.Uploads;
using ShadowDrop.Contracts;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using JsonSerializer = System.Text.Json.JsonSerializer;

[TestFixture]
public sealed class MultipartUploadRequestReaderTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Test]
    public async Task ReadAsync_ShouldRejectOversizedMetadataWhileReading()
    {
        var metadata = CreateValidMetadataPayload(new('a', 200));
        var request = await CreateRequestAsync(metadata, [1, 2, 3, 4], null);

        var action = async () => await MultipartUploadRequestReader.ReadAsync(request, CancellationToken.None, 4096, 64);

        var exceptionAssertion = await action.Should().ThrowAsync<UploadValidationException>();
        exceptionAssertion.Which.Message.Should().Be("The metadata section is too large.");
    }

    [Test]
    public async Task ReadAsync_ShouldRejectOversizedBodiesWithoutContentLength()
    {
        var encryptedContent = Enumerable.Range(0, 96).Select(value => (Byte)value).ToArray();
        var metadata = CreateValidMetadataPayload(plaintextLength: 80,
                                                  encryptedLength: encryptedContent.LongLength,
                                                  chunkSize: 80,
                                                  chunkCount: 1);
        var request = await CreateRequestAsync(metadata, encryptedContent, null);

        var action = async () => await MultipartUploadRequestReader.ReadAsync(request, CancellationToken.None, 128, 4096);

        await action.Should().ThrowAsync<UploadPayloadTooLargeException>();
    }

    [Test]
    public async Task ReadAsync_ShouldMapMetadataArithmeticOverflowToValidationError()
    {
        var metadata = CreateValidMetadataPayload(plaintextLength: Int64.MaxValue,
                                                  encryptedLength: Int64.MaxValue,
                                                  chunkSize: 1,
                                                  chunkCount: Int64.MaxValue);
        var request = await CreateRequestAsync(metadata, [1], null);

        var action = async () => await MultipartUploadRequestReader.ReadAsync(request, CancellationToken.None, 4096, 4096);

        var exceptionAssertion = await action.Should().ThrowAsync<UploadValidationException>();
        exceptionAssertion.Which.Message.Should().Be("Upload metadata is internally inconsistent.");
    }

    [Test]
    public async Task ReadAsync_ShouldRejectMissingReservedFileId()
    {
        var metadata = CreateValidMetadataPayload() with
        {
            FileId = Guid.Empty
        };
        var request = await CreateRequestAsync(metadata, Enumerable.Range(0, 32).Select(value => (Byte)value).ToArray(), null);

        var action = async () => await MultipartUploadRequestReader.ReadAsync(request, CancellationToken.None, 4096, 4096);

        var exceptionAssertion = await action.Should().ThrowAsync<UploadValidationException>();
        exceptionAssertion.Which.Message.Should().Be("The reserved file id is required.");
    }

    [Test]
    public async Task ReadAsync_ShouldReject_WhenContentTypeIsNotMultipart()
    {
        var context = new DefaultHttpContext();
        context.Request.Body = new MemoryStream([1, 2, 3]);
        context.Request.ContentType = "application/json";

        var action = async () => await MultipartUploadRequestReader.ReadAsync(context.Request, CancellationToken.None, 4096, 4096);

        (await action.Should().ThrowAsync<UploadValidationException>()).WithMessage("Uploads must use multipart/form-data.");
    }

    [Test]
    public async Task ReadAsync_ShouldReject_WhenContentLengthExceedsLimit()
    {
        var metadata = CreateValidMetadataPayload();
        var request = await CreateRequestAsync(metadata, [1, 2, 3, 4], 4096);

        var action = async () => await MultipartUploadRequestReader.ReadAsync(request, CancellationToken.None, 128, 4096);

        await action.Should().ThrowAsync<UploadPayloadTooLargeException>();
    }

    [Test]
    public async Task ReadAsync_ShouldReject_WhenMetadataJsonIsInvalid()
    {
        var request = await CreateRawMetadataRequestAsync("{ not valid json");

        var action = async () => await MultipartUploadRequestReader.ReadAsync(request, CancellationToken.None, 4096, 4096);

        (await action.Should().ThrowAsync<UploadValidationException>()).WithMessage("The metadata section is invalid.");
    }

    [Test]
    public async Task ReadAsync_ShouldValidateContentLengthAgainstMetadata_WhenStreamIsConsumed()
    {
        // Declared encrypted length is 32 but only 16 bytes of content are provided.
        var metadata = CreateValidMetadataPayload();
        var request = await CreateRequestAsync(metadata, Enumerable.Range(0, 16).Select(value => (Byte)value).ToArray(), null);
        var (_, content) = await MultipartUploadRequestReader.ReadAsync(request, CancellationToken.None, 4096, 4096);

        var action = async () =>
        {
            using var sink = new MemoryStream();
            await content.CopyToAsync(sink);
        };

        (await action.Should().ThrowAsync<UploadValidationException>())
            .WithMessage("The encrypted content length does not match the declared metadata.");
    }

    [TestCaseSource(nameof(ValidationCases))]
    public async Task ReadAsync_ShouldRejectInvalidMetadata(UploadMetadataPayload metadata, String expectedMessage)
    {
        var request = await CreateRequestAsync(metadata, Enumerable.Range(0, 32).Select(value => (Byte)value).ToArray(), null);

        var action = async () => await MultipartUploadRequestReader.ReadAsync(request, CancellationToken.None, 4096, 4096);

        (await action.Should().ThrowAsync<UploadValidationException>()).WithMessage(expectedMessage);
    }

    private static IEnumerable<TestCaseData> ValidationCases()
    {
        yield return new(CreateValidMetadataPayload() with
        {
            OriginalFileName = "  "
        }, "The original file name is required.");
        yield return new(CreateValidMetadataPayload() with
        {
            PlaintextLength = -1
        }, "File lengths must be zero or greater.");
        yield return new(CreateValidMetadataPayload() with
        {
            ChunkSize = 0
        }, "Chunk metadata must be positive.");
        yield return new(CreateValidMetadataPayload() with
        {
            PlaintextLength = 0,
            EncryptedLength = 0
        }, "Zero-length uploads are not supported.");
        yield return new(CreateValidMetadataPayload() with
        {
            ChunkCount = 2
        }, "Chunk metadata is internally inconsistent.");
        yield return new(CreateValidMetadataPayload() with
        {
            EncryptedLength = 99
        }, "Encrypted length metadata is internally inconsistent.");
        yield return new(CreateValidMetadataPayload() with
        {
            AlgorithmId = "unknown"
        }, "Unsupported encryption metadata was supplied.");
        yield return new(CreateValidMetadataPayload() with
        {
            KdfSalt = "   "
        }, "The KDF salt is required.");
        yield return new(CreateValidMetadataPayload() with
        {
            KdfSalt = "!!!not-base64!!!"
        }, "The KDF salt must be Base64 encoded.");
        yield return new(CreateValidMetadataPayload() with
        {
            KdfSalt = Convert.ToBase64String(new Byte[16])
        }, "The KDF salt must be exactly 32 bytes.");
        yield return new(CreateValidMetadataPayload() with
                         {
                             PlaintextSha256 = "NOT-HEX"
                         },
                         "The plaintext SHA-256 must be a lowercase hexadecimal digest.");
    }

    private static async Task<HttpRequest> CreateRawMetadataRequestAsync(String metadataJson)
    {
        using var multipartContent = new MultipartFormDataContent();
        var metadataContent = new StringContent(metadataJson, Encoding.UTF8);
        metadataContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json");
        multipartContent.Add(metadataContent, "metadata");
        var fileContent = new ByteArrayContent([1, 2, 3, 4]);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/octet-stream");
        multipartContent.Add(fileContent, "content", "cipher.bin");

        var body = await multipartContent.ReadAsByteArrayAsync();
        var context = new DefaultHttpContext();
        context.Request.Body = new MemoryStream(body);
        context.Request.ContentType = multipartContent.Headers.ContentType!.ToString();
        return context.Request;
    }

    private static async Task<HttpRequest> CreateRequestAsync(UploadMetadataPayload metadata,
                                                              Byte[] encryptedContent,
                                                              Int64? contentLength)
    {
        using var multipartContent = new MultipartFormDataContent();
        var metadataContent = new StringContent(JsonSerializer.Serialize(metadata, JsonOptions), Encoding.UTF8);
        metadataContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json");
        multipartContent.Add(metadataContent, "metadata");

        var fileContent = new ByteArrayContent(encryptedContent);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/octet-stream");
        multipartContent.Add(fileContent, "content", "cipher.bin");

        var body = await multipartContent.ReadAsByteArrayAsync();
        var context = new DefaultHttpContext();
        context.Request.Body = new MemoryStream(body);
        context.Request.ContentType = multipartContent.Headers.ContentType!.ToString();
        context.Request.ContentLength = contentLength;
        return context.Request;
    }

    private static UploadMetadataPayload CreateValidMetadataPayload(String? originalFileName = null,
                                                                    Int64 plaintextLength = 16,
                                                                    Int64 encryptedLength = 32,
                                                                    Int32 chunkSize = 16,
                                                                    Int64 chunkCount = 1)
        => new(Guid.NewGuid(),
               originalFileName ?? "cipher.bin",
               plaintextLength,
               encryptedLength,
               "application/octet-stream",
               FormatConstants.EncryptionFormatVersion,
               FormatConstants.Aes256GcmAlgorithmId,
               chunkSize,
               chunkCount,
               Convert.ToBase64String(Enumerable.Range(0, 32).Select(value => (Byte)value).ToArray()),
               new('a', 64));

    public sealed record UploadMetadataPayload(
        Guid FileId,
        String OriginalFileName,
        Int64 PlaintextLength,
        Int64 EncryptedLength,
        String ContentType,
        String EncryptionFormatVersion,
        String AlgorithmId,
        Int32 ChunkSize,
        Int64 ChunkCount,
        String KdfSalt,
        String? PlaintextSha256);
}
