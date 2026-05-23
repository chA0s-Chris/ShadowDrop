// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Uploads;

using FluentAssertions;
using NUnit.Framework;
using ShadowDrop.Cli.Uploads;
using ShadowDrop.Crypto;
using System.Net;
using System.Net.Http.Json;
using System.Text;

[TestFixture]
public sealed class UploadApiClientTests
{
    private static readonly Uri ServerUrl = new("https://shadowdrop.test/");

    [Test]
    public async Task ReserveFileIdAsync_ShouldRetryTransientStatus_AndSucceed()
    {
        var handler = new SequenceHttpMessageHandler(
            _ => new(HttpStatusCode.ServiceUnavailable),
            _ => new(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    fileId = Guid.NewGuid()
                })
            });
        using var httpClient = new HttpClient(handler);
        var sut = new UploadApiClient(httpClient);

        var fileId = await sut.ReserveFileIdAsync(ServerUrl, "token", CancellationToken.None);

        fileId.Should().NotBe(Guid.Empty);
        handler.RequestCount.Should().Be(2);
    }

    [Test]
    public async Task ReserveFileIdAsync_ShouldNotRetryPermanentUnauthorizedFailure()
    {
        var handler = new SequenceHttpMessageHandler(_ => new(HttpStatusCode.Unauthorized));
        using var httpClient = new HttpClient(handler);
        var sut = new UploadApiClient(httpClient);

        var act = () => sut.ReserveFileIdAsync(ServerUrl, "token", CancellationToken.None);

        await act.Should().ThrowAsync<UploadCommandException>()
                 .WithMessage("Authentication token invalid or missing.");
        handler.RequestCount.Should().Be(1);
    }

    [Test]
    public async Task UploadAsync_ShouldRetryTransientNetworkFailure_AndSucceed()
    {
        using var shareSecret = ShareSecret.Generate();
        using var fixture = new UploadFilePlanFixture();
        var handler = new SequenceHttpMessageHandler(
            _ => throw new HttpRequestException("temporary"),
            _ => new(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    fileId = fixture.FileId
                })
            });
        using var httpClient = new HttpClient(handler);
        var sut = new UploadApiClient(httpClient);

        var uploadedFileId = await sut.UploadAsync(ServerUrl, "token", fixture.Plan, shareSecret, CancellationToken.None);

        uploadedFileId.Should().Be(fixture.FileId);
        handler.RequestCount.Should().Be(2);
    }

    [TestCase("{\"fileId\":\"not-a-guid\"}")]
    [TestCase("{")]
    [TestCase("{}")]
    public async Task ReserveFileIdAsync_ShouldFailClosed_WhenSuccessPayloadIsMalformed(String payload)
    {
        var handler = new SequenceHttpMessageHandler(_ => new(HttpStatusCode.OK)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        });
        using var httpClient = new HttpClient(handler);
        var sut = new UploadApiClient(httpClient);

        var act = () => sut.ReserveFileIdAsync(ServerUrl, "token", CancellationToken.None);

        await act.Should().ThrowAsync<UploadCommandException>()
                 .WithMessage("Upload failed; please verify file and try again.");
        handler.RequestCount.Should().Be(1);
    }

    [TestCase("{\"fileId\":\"not-a-guid\"}")]
    [TestCase("{")]
    [TestCase("{}")]
    public async Task UploadAsync_ShouldFailClosed_WhenSuccessPayloadIsMalformed(String payload)
    {
        using var shareSecret = ShareSecret.Generate();
        using var fixture = new UploadFilePlanFixture();
        var handler = new SequenceHttpMessageHandler(_ => new(HttpStatusCode.OK)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        });
        using var httpClient = new HttpClient(handler);
        var sut = new UploadApiClient(httpClient);

        var act = () => sut.UploadAsync(ServerUrl, "token", fixture.Plan, shareSecret, CancellationToken.None);

        await act.Should().ThrowAsync<UploadCommandException>()
                 .WithMessage("Upload failed; please verify file and try again.");
        handler.RequestCount.Should().Be(1);
    }

    private sealed class SequenceHttpMessageHandler(params Func<HttpRequestMessage, HttpResponseMessage>[] responses) : HttpMessageHandler
    {
        private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses = new(responses);

        public Int32 RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            if (_responses.Count == 0)
            {
                throw new AssertionException("Unexpected extra HTTP request.");
            }

            return Task.FromResult(_responses.Dequeue()(request));
        }
    }

    private sealed class UploadFilePlanFixture : IDisposable
    {
        private readonly String _rootDirectory =
            Path.Combine(TestContext.CurrentContext.WorkDirectory, "artifacts", "cli-upload-api-client-tests", Guid.NewGuid().ToString("N"));

        public UploadFilePlanFixture()
        {
            Directory.CreateDirectory(_rootDirectory);
            FileId = Guid.NewGuid();
            FilePath = Path.Combine(_rootDirectory, "payload.bin");
            File.WriteAllBytes(FilePath, Enumerable.Range(0, 64).Select(static value => (Byte)value).ToArray());
            var kdfSalt = FileEncryptionContext.GenerateKdfSalt();
            var fileInfo = new FileInfo(FilePath);
            var chunkSize = 1024 * 1024;
            var plaintextLength = fileInfo.Length;
            var chunkCount = 1L;
            var encryptedLength = plaintextLength + 16;
            Plan = new UploadFilePlan(
                fileInfo,
                FileId,
                new FileEncryptionContext(FileId, kdfSalt),
                new UploadMetadataPayload(
                    FileId,
                    fileInfo.Name,
                    plaintextLength,
                    encryptedLength,
                    "application/octet-stream",
                    "1",
                    "aes-256-gcm",
                    chunkSize,
                    chunkCount,
                    Convert.ToBase64String(kdfSalt),
                    null),
                chunkSize);
        }

        public Guid FileId { get; }

        public String FilePath { get; }

        public UploadFilePlan Plan { get; }

        public void Dispose()
        {
            if (Directory.Exists(_rootDirectory))
            {
                Directory.Delete(_rootDirectory, true);
            }
        }
    }
}
