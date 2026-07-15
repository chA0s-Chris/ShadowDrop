// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Uploads;

using FluentAssertions;
using NUnit.Framework;
using ShadowDrop.Cli.Configuration;
using ShadowDrop.Cli.Uploads;
using ShadowDrop.Crypto;
using ShadowDrop.Tests.Fakes;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

[TestFixture]
public sealed class UploadApiClientTests
{
    private static readonly Uri ServerUrl = new("https://shadowdrop.test/");

    [Test]
    public async Task GetCapabilitiesAsync_ShouldUseScopedUploadRouteAndBearerToken()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            request.Method.Should().Be(HttpMethod.Get);
            request.RequestUri.Should().Be(new Uri(ServerUrl, "/api/uploads/capabilities"));
            request.Headers.Authorization!.Parameter.Should().Be("upload-token");
            return new(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    maxFilePayloadBytes = 4096
                })
            };
        }));
        var sut = new UploadApiClient(httpClient);

        var result = await sut.GetCapabilitiesAsync(ServerUrl, "upload-token", CancellationToken.None);

        result.MaxFilePayloadBytes.Should().Be(4096);
    }

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

        var uploadedFileId = await sut.UploadAsync(ServerUrl, "token", fixture.Plan, shareSecret, null, CancellationToken.None);

        uploadedFileId.Should().Be(fixture.FileId);
        handler.RequestCount.Should().Be(2);
    }

    [Test]
    public async Task ReserveFileIdAsync_ShouldNormalizeFinalHttpRequestException()
    {
        var handler = new SequenceHttpMessageHandler(
            _ => throw new HttpRequestException("temporary-1"),
            _ => throw new HttpRequestException("temporary-2"),
            _ => throw new HttpRequestException("temporary-3"));
        using var httpClient = new HttpClient(handler);
        var sut = new UploadApiClient(httpClient);

        var act = () => sut.ReserveFileIdAsync(ServerUrl, "token", CancellationToken.None);

        await act.Should().ThrowAsync<UploadCommandException>()
                 .WithMessage("Server connection failed.");
        handler.RequestCount.Should().Be(3);
    }

    [Test]
    public async Task UploadAsync_ShouldNormalizeFinalTimeoutException()
    {
        using var shareSecret = ShareSecret.Generate();
        using var fixture = new UploadFilePlanFixture();
        var handler = new SequenceHttpMessageHandler(
            _ => throw new TaskCanceledException("timeout-1"),
            _ => throw new TaskCanceledException("timeout-2"),
            _ => throw new TaskCanceledException("timeout-3"));
        using var httpClient = new HttpClient(handler);
        var sut = new UploadApiClient(httpClient);

        var act = () => sut.UploadAsync(ServerUrl, "token", fixture.Plan, shareSecret, null, CancellationToken.None);

        await act.Should().ThrowAsync<UploadCommandException>()
                 .WithMessage("Server connection failed.");
        handler.RequestCount.Should().Be(3);
    }

    [Test]
    public async Task UploadAsync_ShouldUseExponentialBackoffBetweenRetries()
    {
        using var shareSecret = ShareSecret.Generate();
        using var fixture = new UploadFilePlanFixture();
        var delays = new List<TimeSpan>();
        var handler = new SequenceHttpMessageHandler(
            _ => new(HttpStatusCode.TooManyRequests),
            _ => throw new HttpRequestException("temporary"),
            _ => new(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    fileId = fixture.FileId
                })
            });
        using var httpClient = new HttpClient(handler);
        var sut = new UploadApiClient(httpClient, (delay, cancellationToken) =>
        {
            delays.Add(delay);
            return Task.CompletedTask;
        });

        var uploadedFileId = await sut.UploadAsync(ServerUrl, "token", fixture.Plan, shareSecret, null, CancellationToken.None);

        uploadedFileId.Should().Be(fixture.FileId);
        delays.Should().Equal(TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(400));
        handler.RequestCount.Should().Be(3);
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

        var act = () => sut.UploadAsync(ServerUrl, "token", fixture.Plan, shareSecret, null, CancellationToken.None);

        await act.Should().ThrowAsync<UploadCommandException>()
                 .WithMessage("Upload failed; please verify file and try again.");
        handler.RequestCount.Should().Be(1);
    }

    [Test]
    public async Task UploadAsync_ShouldPropagateCallerCancellation_WithoutRetrying()
    {
        using var shareSecret = ShareSecret.Generate();
        using var fixture = new UploadFilePlanFixture();
        using var cancellationTokenSource = new CancellationTokenSource();
        var handler = new SequenceHttpMessageHandler(_ =>
        {
            cancellationTokenSource.Cancel();
            throw new TaskCanceledException("caller cancelled");
        });
        using var httpClient = new HttpClient(handler);
        var sut = new UploadApiClient(httpClient);

        var act = () => sut.UploadAsync(ServerUrl, "token", fixture.Plan, shareSecret, null, cancellationTokenSource.Token);

        await act.Should().ThrowAsync<TaskCanceledException>();
        handler.RequestCount.Should().Be(1);
    }

    [Test]
    public async Task UploadAsync_ShouldRetryInactivityTimeout_AsTransientFailure()
    {
        using var shareSecret = ShareSecret.Generate();
        using var fixture = new UploadFilePlanFixture();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var stallEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new StallFirstAttemptHandler(fixture.FileId, stallEntered);
        using var httpClient = new HttpClient(handler);
        var sut = new UploadApiClient(httpClient, static (_, _) => Task.CompletedTask, timeProvider);

        var uploadTask = sut.UploadAsync(ServerUrl, "token", fixture.Plan, shareSecret, null, CancellationToken.None);
        await stallEntered.Task;
        timeProvider.Advance(UploadAttemptTimeout.InactivityTimeout);
        var uploadedFileId = await uploadTask;

        uploadedFileId.Should().Be(fixture.FileId);
        handler.RequestCount.Should().Be(2);
    }

    [Test]
    public void UploadMetadataPayloadSerialization_ShouldPreserveMultipartContract()
    {
        var fileId = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var payload = new UploadMetadataPayload(fileId,
                                                "payload.bin",
                                                128,
                                                144,
                                                "application/octet-stream",
                                                "1",
                                                "aes-256-gcm",
                                                64,
                                                2,
                                                Convert.ToBase64String(Enumerable.Range(0, 32).Select(static value => (Byte)value).ToArray()),
                                                null);

        var json = JsonSerializer.Serialize(payload, CliJsonSerializerContext.Default.UploadMetadataPayload);
        using var document = JsonDocument.Parse(json);

        document.RootElement.EnumerateObject().Select(static property => property.Name).Should()
                .Equal("fileId",
                       "originalFileName",
                       "plaintextLength",
                       "encryptedLength",
                       "contentType",
                       "encryptionFormatVersion",
                       "algorithmId",
                       "chunkSize",
                       "chunkCount",
                       "kdfSalt",
                       "plaintextSha256");
        document.RootElement.GetProperty("fileId").GetGuid().Should().Be(fileId);
        document.RootElement.GetProperty("originalFileName").GetString().Should().Be("payload.bin");
        document.RootElement.GetProperty("plaintextSha256").ValueKind.Should().Be(JsonValueKind.Null);
    }

    private sealed class StallFirstAttemptHandler(Guid fileId, TaskCompletionSource stallEntered) : HttpMessageHandler
    {
        public Int32 RequestCount { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            if (RequestCount == 1)
            {
                stallEntered.SetResult();
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            return new(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    fileId
                })
            };
        }
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
            var encryptedLength = plaintextLength + EncryptedChunk.AuthenticationTagLength;
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
