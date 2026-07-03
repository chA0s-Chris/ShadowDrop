// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Downloads;

using FluentAssertions;
using NUnit.Framework;
using ShadowDrop.Cli.Downloads;
using ShadowDrop.Contracts;
using ShadowDrop.Crypto;
using System.Net;
using System.Security.Cryptography;

public sealed class CliDownloadSessionTests
{
    [Test]
    public void Constructor_ShouldRejectNonSeekableDestination_WhenResuming()
    {
        var fixture = DownloadFixture.Create();
        using var httpClient =
            new HttpClient(new StubHttpMessageHandler(_ => throw new InvalidOperationException("The session must not send a request during construction.")))
            {
                BaseAddress = new("https://shadowdrop.test/")
            };
        using var destination = new NonSeekableWriteStream();
        using var shareSecret = ShareSecret.FromBytes(fixture.KeyMaterial);

        var act = () => new CliDownloadSession(httpClient,
                                               new(httpClient.BaseAddress, "d/share-token/files/file-id"),
                                               destination,
                                               shareSecret,
                                               fixture.CreateFileEncryptionContext(),
                                               durablePlaintextLength: fixture.ChunkSize,
                                               totalPlaintextSize: fixture.Plaintext.LongLength);

        act.Should().Throw<ArgumentException>()
           .WithMessage("*resumed download requires a seekable destination stream*")
           .And.ParamName.Should().Be("destination");
    }

    [Test]
    public void Constructor_ShouldReportDurablePlaintextLengthImmediately_WhenResuming()
    {
        var fixture = DownloadFixture.Create();
        using var httpClient =
            new HttpClient(new StubHttpMessageHandler(_ => throw new InvalidOperationException("The session must not send a request during construction.")))
            {
                BaseAddress = new("https://shadowdrop.test/")
            };
        using var destination = new MemoryStream(fixture.Plaintext.Take(fixture.ChunkSize).ToArray());
        using var shareSecret = ShareSecret.FromBytes(fixture.KeyMaterial);
        List<Int64> reportedValues = [];
        var progress = new RecordingProgress(reportedValues.Add);

        using var session = new CliDownloadSession(httpClient,
                                                   new(httpClient.BaseAddress, "d/share-token/files/file-id"),
                                                   destination,
                                                   shareSecret,
                                                   fixture.CreateFileEncryptionContext(),
                                                   durablePlaintextLength: fixture.ChunkSize,
                                                   totalPlaintextSize: fixture.Plaintext.LongLength,
                                                   progress: progress);

        reportedValues.Should().ContainSingle().Which.Should().Be(fixture.ChunkSize);
        session.DurablePlaintextLength.Should().Be(fixture.ChunkSize);
    }

    [TestCase(-1)]
    [TestCase(1)]
    public async Task DownloadAsync_ShouldFailClosed_WhenSeekableDestinationLengthDoesNotMatchDurablePlaintextLength(Int32 lengthDelta)
    {
        var fixture = DownloadFixture.Create();
        var firstChunkEncryptedLength = fixture.EncryptedChunks[0].Length;
        using var handler = new StubHttpMessageHandler(_ => fixture.CreateInterruptedResponse(firstChunkEncryptedLength + 7));
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new("https://shadowdrop.test/")
        };
        await using var destination = new MemoryStream();
        using var shareSecret = ShareSecret.FromBytes(fixture.KeyMaterial);
        using var session = new CliDownloadSession(httpClient, new(httpClient.BaseAddress, "d/share-token/files/file-id"), destination, shareSecret,
                                                   fixture.CreateFileEncryptionContext());

        var firstAttempt = async () => await session.DownloadAsync(CancellationToken.None);

        await firstAttempt.Should().ThrowAsync<IOException>();
        session.TotalPlaintextSize.Should().Be(fixture.Plaintext.LongLength);
        session.DurablePlaintextLength.Should().Be(fixture.ChunkSize);
        destination.Length.Should().Be(fixture.ChunkSize);

        destination.SetLength(fixture.ChunkSize + lengthDelta);

        var resumedAttempt = async () => await session.DownloadAsync(CancellationToken.None);

        await resumedAttempt.Should().ThrowAsync<InvalidOperationException>()
                            .WithMessage("*seekable destination length does not match the durable plaintext length*");
        handler.Requests.Should().HaveCount(1);
        destination.Length.Should().Be(fixture.ChunkSize + lengthDelta);
        session.DurablePlaintextLength.Should().Be(fixture.ChunkSize);
    }

    [Test]
    public async Task DownloadAsync_ShouldRequestCliMode_DecryptFullResponse_AndPersistPlaintext()
    {
        var fixture = DownloadFixture.Create();
        using var handler = new StubHttpMessageHandler(_ => fixture.CreateSuccessResponse());
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new("https://shadowdrop.test/")
        };
        await using var destination = new MemoryStream();
        using var shareSecret = ShareSecret.FromBytes(fixture.KeyMaterial);
        using var session = new CliDownloadSession(httpClient, new(httpClient.BaseAddress, "d/share-token/files/file-id"), destination, shareSecret,
                                                   fixture.CreateFileEncryptionContext());

        await session.DownloadAsync(CancellationToken.None);

        handler.Requests.Should().ContainSingle();
        handler.Requests[0].RequestUri!.Query.Should().Be("?mode=cli");
        handler.Requests[0].Headers.Range.Should().BeNull();
        destination.ToArray().Should().Equal(fixture.Plaintext);
        session.DurablePlaintextLength.Should().Be(fixture.Plaintext.LongLength);
        session.TotalPlaintextSize.Should().Be(fixture.Plaintext.LongLength);
    }

    [Test]
    public async Task DownloadAsync_ShouldResumeFromLastDurablePlaintextByte_AfterInterruptedChunkStream()
    {
        var fixture = DownloadFixture.Create();
        var firstChunkEncryptedLength = fixture.EncryptedChunks[0].Length;
        var callCount = 0;
        using var handler = new StubHttpMessageHandler(request =>
        {
            callCount++;
            if (callCount == 1)
            {
                return fixture.CreateInterruptedResponse(firstChunkEncryptedLength + 7);
            }

            return fixture.CreateSuccessResponse(new()
            {
                Start = fixture.ChunkSize,
                End = fixture.Plaintext.LongLength
            });
        });
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new("https://shadowdrop.test/")
        };
        await using var destination = new MemoryStream();
        using var shareSecret = ShareSecret.FromBytes(fixture.KeyMaterial);
        using var session = new CliDownloadSession(httpClient, new(httpClient.BaseAddress, "d/share-token/files/file-id"), destination, shareSecret,
                                                   fixture.CreateFileEncryptionContext());

        var firstAttempt = async () => await session.DownloadAsync(CancellationToken.None);

        await firstAttempt.Should().ThrowAsync<IOException>();
        session.TotalPlaintextSize.Should().Be(fixture.Plaintext.LongLength);
        session.DurablePlaintextLength.Should().Be(fixture.ChunkSize);
        destination.ToArray().Should().Equal(fixture.Plaintext.Take(fixture.ChunkSize));

        await session.DownloadAsync(CancellationToken.None);

        handler.Requests.Should().HaveCount(2);
        handler.Requests[1].RequestUri!.Query.Should().Be("?mode=cli");
        var resumedRange = handler.Requests[1].Headers.Range;
        resumedRange.Should().NotBeNull();
        resumedRange!.Ranges.Should().ContainSingle();
        resumedRange.Ranges.Single().From.Should().Be(fixture.ChunkSize);
        resumedRange.Ranges.Single().To.Should().Be(fixture.Plaintext.LongLength - 1);
        destination.ToArray().Should().Equal(fixture.Plaintext);
        session.DurablePlaintextLength.Should().Be(fixture.Plaintext.LongLength);
    }

    [Test]
    public async Task DownloadAsync_ShouldThrowCryptographicException_WhenServerRelabelsNonFinalChunkAsFinal()
    {
        var fixture = DownloadFixture.Create();
        using var handler = new StubHttpMessageHandler(_ => fixture.CreateRelabeledTruncatedResponse(1));
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new("https://shadowdrop.test/")
        };
        await using var destination = new MemoryStream();
        using var shareSecret = ShareSecret.FromBytes(fixture.KeyMaterial);
        using var session = new CliDownloadSession(httpClient, new(httpClient.BaseAddress, "d/share-token/files/file-id"), destination, shareSecret,
                                                   fixture.CreateFileEncryptionContext());

        var act = async () => await session.DownloadAsync(CancellationToken.None);

        await act.Should().ThrowAsync<CryptographicException>();
        destination.Length.Should().Be(0);
        session.DurablePlaintextLength.Should().Be(0);
    }

    [Test]
    public async Task DownloadAsync_ShouldThrowInvalidDataException_WhenResponseContainsTrailingDataAfterFinalChunk()
    {
        var fixture = DownloadFixture.Create();
        using var handler = new StubHttpMessageHandler(_ => fixture.CreateResponseWithTrailingData(4));
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new("https://shadowdrop.test/")
        };
        await using var destination = new MemoryStream();
        using var shareSecret = ShareSecret.FromBytes(fixture.KeyMaterial);
        using var session = new CliDownloadSession(httpClient, new(httpClient.BaseAddress, "d/share-token/files/file-id"), destination, shareSecret,
                                                   fixture.CreateFileEncryptionContext());

        var act = async () => await session.DownloadAsync(CancellationToken.None);

        await act.Should().ThrowAsync<InvalidDataException>();
    }

    private sealed class DownloadFixture
    {
        private DownloadFixture(Guid fileId, Byte[] keyMaterial, Byte[] kdfSalt, Byte[] plaintext, Int32 chunkSize, Byte[][] encryptedChunks)
        {
            FileId = fileId;
            KeyMaterial = keyMaterial;
            KdfSalt = kdfSalt;
            Plaintext = plaintext;
            ChunkSize = chunkSize;
            EncryptedChunks = encryptedChunks;
        }

        public Int32 ChunkSize { get; }

        public Byte[][] EncryptedChunks { get; }

        public Guid FileId { get; }

        public Byte[] KdfSalt { get; }

        public Byte[] KeyMaterial { get; }

        public Byte[] Plaintext { get; }

        public static DownloadFixture Create()
        {
            var fileId = Guid.Parse("01234567-89ab-cdef-0123-456789abcdef");
            var keyMaterial = Enumerable.Range(1, 32).Select(static value => (Byte)value).ToArray();
            var kdfSalt = Enumerable.Range(65, 32).Select(static value => (Byte)value).ToArray();
            var plaintext = Enumerable.Range(0, 128).Select(static value => (Byte)(255 - value)).ToArray();
            const Int32 chunkSize = 64;
            using var shareSecret = ShareSecret.FromBytes(keyMaterial);
            var encryptionContext = new FileEncryptionContext(fileId, kdfSalt);
            using var contentKey = ChunkEncryptionService.DeriveContentKey(shareSecret, encryptionContext);
            var encryptedChunks = new List<Byte[]>();
            var chunkCount = plaintext.LongLength / chunkSize;

            for (var chunkIndex = 0L; chunkIndex < chunkCount; chunkIndex++)
            {
                var chunkPlaintext = plaintext.Skip(checked((Int32)(chunkIndex * chunkSize))).Take(chunkSize).ToArray();
                var encryptedChunk = ChunkEncryptionService.EncryptChunk(chunkPlaintext,
                                                                         contentKey,
                                                                         new(CryptoVersion.V1,
                                                                             CryptoAlgorithm.Aes256Gcm,
                                                                             fileId,
                                                                             chunkSize,
                                                                             chunkIndex,
                                                                             chunkPlaintext.Length,
                                                                             chunkIndex == chunkCount - 1));
                encryptedChunks.Add(encryptedChunk.Ciphertext);
            }

            return new(fileId, keyMaterial, kdfSalt, plaintext, chunkSize, encryptedChunks.ToArray());
        }

        public FileEncryptionContext CreateFileEncryptionContext() => new(FileId, KdfSalt);

        public HttpResponseMessage CreateInterruptedResponse(Int32 bytesBeforeFailure) =>
            CreateResponse(new InterruptingStream(CreateResponseBody(), bytesBeforeFailure));

        public HttpResponseMessage CreateRelabeledTruncatedResponse(Int32 servedFullSizedChunkCount)
        {
            var declaredLength = servedFullSizedChunkCount * ChunkSize;
            var responseBody = EncryptedChunks.Take(servedFullSizedChunkCount).SelectMany(static chunk => chunk).ToArray();
            return CreateResponse(new MemoryStream(responseBody, false),
                                  new()
                                  {
                                      Start = 0,
                                      End = declaredLength
                                  },
                                  responseBody.LongLength,
                                  declaredLength);
        }

        public HttpResponseMessage CreateResponseWithTrailingData(Int32 trailingByteCount)
        {
            var range = new RequestedPlaintextRangeContract
            {
                Start = 0,
                End = Plaintext.LongLength
            };
            var responseBody = CreateResponseBody(range).Concat(Enumerable.Repeat((Byte)0xAB, trailingByteCount)).ToArray();
            return CreateResponse(new MemoryStream(responseBody, false), range, responseBody.LongLength);
        }

        public HttpResponseMessage CreateSuccessResponse(RequestedPlaintextRangeContract? requestedRange = null)
        {
            var range = requestedRange ?? new RequestedPlaintextRangeContract
            {
                Start = 0,
                End = Plaintext.LongLength
            };
            var responseBody = CreateResponseBody(range);
            return CreateResponse(new MemoryStream(responseBody, false), range, responseBody.LongLength);
        }

        private HttpResponseMessage CreateResponse(Stream contentStream,
                                                   RequestedPlaintextRangeContract? requestedRange = null,
                                                   Int64? contentLength = null,
                                                   Int64? totalPlaintextSize = null)
        {
            var range = requestedRange ?? new RequestedPlaintextRangeContract
            {
                Start = 0,
                End = Plaintext.LongLength
            };
            var chunkRange = ChunkEncryptionService.GetChunkRange(range.Start, range.End - range.Start, ChunkSize);
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(contentStream)
            };
            response.Content.Headers.ContentType = new(DownloadHeaderConstants.CliDownloadContentType);
            response.Content.Headers.ContentLength = contentLength ?? CreateResponseBody(range).LongLength;
            response.Headers.TryAddWithoutValidation(DownloadHeaderConstants.FirstChunkIndexHeaderName, chunkRange.FirstChunkIndex.ToString());
            response.Headers.TryAddWithoutValidation(DownloadHeaderConstants.LastChunkIndexHeaderName, chunkRange.LastChunkIndex.ToString());
            response.Headers.TryAddWithoutValidation(DownloadHeaderConstants.PlaintextRangeStartHeaderName, range.Start.ToString());
            response.Headers.TryAddWithoutValidation(DownloadHeaderConstants.PlaintextRangeEndHeaderName, range.End.ToString());
            response.Headers.TryAddWithoutValidation(DownloadHeaderConstants.TotalPlaintextSizeHeaderName,
                                                     (totalPlaintextSize ?? Plaintext.LongLength).ToString());
            response.Headers.TryAddWithoutValidation(DownloadHeaderConstants.ChunkSizeHeaderName, ChunkSize.ToString());
            response.Headers.TryAddWithoutValidation(DownloadHeaderConstants.FinalChunkPlaintextLengthHeaderName, ChunkSize.ToString());
            return response;
        }

        private Byte[] CreateResponseBody(RequestedPlaintextRangeContract? requestedRange = null)
        {
            var range = requestedRange ?? new RequestedPlaintextRangeContract
            {
                Start = 0,
                End = Plaintext.LongLength
            };
            var chunkRange = ChunkEncryptionService.GetChunkRange(range.Start, range.End - range.Start, ChunkSize);
            return EncryptedChunks.Skip(checked((Int32)chunkRange.FirstChunkIndex))
                                  .Take(checked((Int32)(chunkRange.LastChunkIndex - chunkRange.FirstChunkIndex + 1)))
                                  .SelectMany(static chunk => chunk)
                                  .ToArray();
        }
    }

    private sealed class InterruptingStream : Stream
    {
        private readonly Int32 _bytesBeforeFailure;
        private readonly Stream _inner;
        private Int32 _bytesRead;

        public InterruptingStream(Byte[] content, Int32 bytesBeforeFailure)
        {
            _inner = new MemoryStream(content, false);
            _bytesBeforeFailure = bytesBeforeFailure;
        }

        public override Boolean CanRead => true;
        public override Boolean CanSeek => false;
        public override Boolean CanWrite => false;
        public override Int64 Length => _inner.Length;

        public override Int64 Position
        {
            get => _inner.Position;
            set => throw new NotSupportedException();
        }

        public override void Flush() => throw new NotSupportedException();

        public override Int32 Read(Byte[] buffer, Int32 offset, Int32 count) =>
            ReadAsync(buffer.AsMemory(offset, count), CancellationToken.None).AsTask().GetAwaiter().GetResult();

        public override Task<Int32> ReadAsync(Byte[] buffer, Int32 offset, Int32 count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override async ValueTask<Int32> ReadAsync(Memory<Byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_bytesRead >= _bytesBeforeFailure)
            {
                throw new IOException("Simulated interrupted download.");
            }

            var allowedCount = Math.Min(buffer.Length, _bytesBeforeFailure - _bytesRead);
            var bytesRead = await _inner.ReadAsync(buffer[..allowedCount], cancellationToken);
            _bytesRead += bytesRead;
            return bytesRead;
        }

        public override Int64 Seek(Int64 offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(Int64 value) => throw new NotSupportedException();
        public override void Write(Byte[] buffer, Int32 offset, Int32 count) => throw new NotSupportedException();
    }

    private sealed class NonSeekableWriteStream : Stream
    {
        public override Boolean CanRead => false;
        public override Boolean CanSeek => false;
        public override Boolean CanWrite => true;
        public override Int64 Length => throw new NotSupportedException();

        public override Int64 Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() { }

        public override Int32 Read(Byte[] buffer, Int32 offset, Int32 count) => throw new NotSupportedException();
        public override Int64 Seek(Int64 offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(Int64 value) => throw new NotSupportedException();

        public override void Write(Byte[] buffer, Int32 offset, Int32 count) { }
    }

    private sealed class RecordingProgress(Action<Int64> onReport) : IProgress<Int64>
    {
        public void Report(Int64 value) => onReport(value);
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responseFactory;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        {
            _responseFactory = responseFactory;
        }

        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(CloneRequest(request));
            return Task.FromResult(_responseFactory(request));
        }

        private static HttpRequestMessage CloneRequest(HttpRequestMessage request)
        {
            var clone = new HttpRequestMessage(request.Method, request.RequestUri);
            foreach (var header in request.Headers)
            {
                clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            return clone;
        }
    }
}
