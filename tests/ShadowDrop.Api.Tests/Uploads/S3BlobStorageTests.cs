// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Uploads;

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using ShadowDrop.Api.Configuration;
using ShadowDrop.Api.Uploads;

[TestFixture]
public sealed class S3BlobStorageTests
{
    [Test]
    public async Task ProviderContract_ShouldPass()
    {
        await BlobStorageContract.AssertAsync(CreateStorage(new RecordingS3Client()));
    }

    [TestCase(0)]
    [TestCase(37)]
    public async Task SaveAsync_ShouldUseSingleRequest_ForContentSmallerThanPart(Int32 length)
    {
        var client = new RecordingS3Client();
        var storage = CreateStorage(client, "  tenant//archive/  ");
        var fileId = Guid.NewGuid();
        var content = Enumerable.Range(0, length).Select(static value => (Byte)value).ToArray();

        var descriptor = await storage.SaveAsync(fileId, new NonSeekableReadStream(content), CancellationToken.None);

        descriptor.BlobKey.Should().Be(fileId.ToString("N"));
        descriptor.WrittenLength.Should().Be(length);
        client.PutObjectCount.Should().Be(1);
        client.Objects[$"bucket/tenant//archive/{fileId:N}"].Should().Equal(content);
    }

    [Test]
    public async Task SaveAsync_ShouldUseMultipartAndAbort_WhenLaterPartFails()
    {
        var client = new RecordingS3Client
        {
            FailingPartNumber = 2
        };
        var storage = CreateStorage(client);
        var content = new Byte[S3BlobStorage.MultipartPartSize + 1];
        var save = async () => await storage.SaveAsync(Guid.NewGuid(), new NonSeekableReadStream(content), CancellationToken.None);

        await save.Should().ThrowAsync<IOException>().WithMessage("injected part failure");
        client.AbortCount.Should().Be(1);
        client.PutObjectCount.Should().Be(0);
    }

    [Test]
    public async Task SaveAsync_ShouldPropagateCancellationAndAbortMultipartUpload()
    {
        var client = new RecordingS3Client();
        var storage = CreateStorage(client);
        var content = new Byte[S3BlobStorage.MultipartPartSize + 1];
        using var cancellation = new CancellationTokenSource();
        var save = async () => await storage.SaveAsync(
            Guid.NewGuid(),
            // ReSharper disable AccessToDisposedClosure
            new CancelAfterFirstPartStream(content, cancellation),
            cancellation.Token);
        // ReSharper restore AccessToDisposedClosure

        await save.Should().ThrowAsync<OperationCanceledException>();
        client.AbortCount.Should().Be(1);
    }

    [Test]
    public async Task OpenReadAsync_ShouldReuseSequentialResponseAndOpenRangeAfterSeek()
    {
        var client = new RecordingS3Client();
        var storage = CreateStorage(client, "prefix");
        var fileId = Guid.NewGuid();
        var content = Enumerable.Range(0, 64).Select(static value => (Byte)value).ToArray();
        client.SetObject("bucket", $"prefix/{fileId:N}", content);

        await using var stream = await storage.OpenReadAsync(fileId.ToString("N"), CancellationToken.None);
        var first = new Byte[4];
        var second = new Byte[4];
        _ = await stream.ReadAsync(first);
        _ = await stream.ReadAsync(second);
        _ = stream.Seek(17, SeekOrigin.Begin);
        var afterSeek = new Byte[5];
        _ = await stream.ReadAsync(afterSeek);

        stream.CanSeek.Should().BeTrue();
        client.RangeStarts.Should().Equal(0, 17);
        first.Should().Equal(content[..4]);
        second.Should().Equal(content[4..8]);
        afterSeek.Should().Equal(content[17..22]);
    }

    [Test]
    public async Task OpenReadAsync_ShouldMapMissingObjectToFileNotFoundException()
    {
        var storage = CreateStorage(new RecordingS3Client());
        var blobKey = Guid.NewGuid().ToString("N");
        var open = async () => await storage.OpenReadAsync(blobKey, CancellationToken.None);

        await open.Should().ThrowAsync<FileNotFoundException>();
    }

    [Test]
    public async Task ReadAsync_ShouldMapObjectRemovedAfterOpenToFileNotFoundException()
    {
        var client = new RecordingS3Client();
        var storage = CreateStorage(client);
        var fileId = Guid.NewGuid();
        client.SetObject("bucket", fileId.ToString("N"), [1, 2, 3]);
        await using var stream = await storage.OpenReadAsync(fileId.ToString("N"), CancellationToken.None);
        client.ReadAsMissing = true;
        // ReSharper disable once AccessToDisposedClosure
        var read = async () => await stream.ReadAsync(new Byte[1]);

        await read.Should().ThrowAsync<FileNotFoundException>();
    }

    [Test]
    public async Task DeleteIfExistsAsync_ShouldReportWhetherObjectExisted()
    {
        var client = new RecordingS3Client();
        var storage = CreateStorage(client);
        var fileId = Guid.NewGuid();
        _ = await storage.SaveAsync(fileId, new MemoryStream([1, 2, 3]), CancellationToken.None);

        (await storage.DeleteIfExistsAsync(fileId.ToString("N"), CancellationToken.None)).Should().BeTrue();
        (await storage.DeleteIfExistsAsync(fileId.ToString("N"), CancellationToken.None)).Should().BeFalse();
    }

    private static S3BlobStorage CreateStorage(RecordingS3Client client, String keyPrefix = "")
    {
        var options = new ShadowDropOptions
        {
            Storage = new()
            {
                S3 = new()
                {
                    BucketName = "bucket",
                    KeyPrefix = S3ObjectKey.NormalizePrefix(keyPrefix)
                }
            }
        };
        return new(options, client, NullLogger<S3BlobStorage>.Instance);
    }

    private class NonSeekableReadStream(Byte[] content) : MemoryStream(content, false)
    {
        public override Boolean CanSeek => false;

        public override Int64 Seek(Int64 offset, SeekOrigin loc) => throw new NotSupportedException();
    }

    private sealed class CancelAfterFirstPartStream(Byte[] content, CancellationTokenSource cancellation)
        : NonSeekableReadStream(content)
    {
        public override ValueTask<Int32> ReadAsync(Memory<Byte> buffer, CancellationToken cancellationToken = default)
        {
            if (Position >= S3BlobStorage.MultipartPartSize)
            {
                cancellation.Cancel();
            }

            cancellationToken.ThrowIfCancellationRequested();
            return base.ReadAsync(buffer, cancellationToken);
        }
    }
}
