// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Downloads;

using FluentAssertions;
using NUnit.Framework;
using ShadowDrop.Cli.Downloads;
using ShadowDrop.Contracts;
using ShadowDrop.Crypto;
using System.Net;

public sealed class CliDownloadSessionTests
{
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

            for (var chunkIndex = 0L; chunkIndex < plaintext.LongLength / chunkSize; chunkIndex++)
            {
                var chunkPlaintext = plaintext.Skip(checked((Int32)(chunkIndex * chunkSize))).Take(chunkSize).ToArray();
                var encryptedChunk = ChunkEncryptionService.EncryptChunk(chunkPlaintext,
                                                                         contentKey,
                                                                         new(CryptoVersion.V1,
                                                                             CryptoAlgorithm.Aes256Gcm,
                                                                             fileId,
                                                                             chunkSize,
                                                                             chunkIndex,
                                                                             chunkPlaintext.Length));
                encryptedChunks.Add(encryptedChunk.Ciphertext);
            }

            return new(fileId, keyMaterial, kdfSalt, plaintext, chunkSize, encryptedChunks.ToArray());
        }

        public FileEncryptionContext CreateFileEncryptionContext() => new(FileId, KdfSalt);

        public HttpResponseMessage CreateInterruptedResponse(Int32 bytesBeforeFailure) =>
            CreateResponse(new InterruptingStream(CreateResponseBody(), bytesBeforeFailure));

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
                                                   Int64? contentLength = null)
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
            response.Headers.TryAddWithoutValidation(DownloadHeaderConstants.TotalPlaintextSizeHeaderName, Plaintext.LongLength.ToString());
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
