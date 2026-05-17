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
        => new(originalFileName ?? "cipher.bin",
               plaintextLength,
               encryptedLength,
               "application/octet-stream",
               FormatConstants.EncryptionFormatVersion,
               FormatConstants.Aes256GcmAlgorithmId,
               chunkSize,
               chunkCount,
               Convert.ToBase64String(Enumerable.Range(0, 32).Select(value => (Byte)value).ToArray()),
               new('a', 64));

    private sealed record UploadMetadataPayload(
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
