// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Uploads;

using FluentAssertions;
using ShadowDrop.Api.Uploads;

internal static class BlobStorageContract
{
    public static async Task AssertAsync(IBlobStorage storage)
    {
        var fileId = Guid.NewGuid();
        var content = Enumerable.Range(0, 257).Select(static value => (Byte)(value % 251)).ToArray();
        var descriptor = await storage.SaveAsync(fileId, new MemoryStream(content), CancellationToken.None);
        descriptor.WrittenLength.Should().Be(content.Length);

        await using (var stream = await storage.OpenReadAsync(descriptor.BlobKey, CancellationToken.None))
        {
            stream.CanSeek.Should().BeTrue();
            _ = stream.Seek(123, SeekOrigin.Begin);
            var range = new Byte[17];
            await stream.ReadExactlyAsync(range);
            range.Should().Equal(content[123..140]);

            _ = stream.Seek(0, SeekOrigin.Begin);
            using var copy = new MemoryStream();
            await stream.CopyToAsync(copy);
            copy.ToArray().Should().Equal(content);
        }

        (await storage.DeleteIfExistsAsync(descriptor.BlobKey, CancellationToken.None)).Should().BeTrue();
        (await storage.DeleteIfExistsAsync(descriptor.BlobKey, CancellationToken.None)).Should().BeFalse();
        var openMissing = async () => await storage.OpenReadAsync(descriptor.BlobKey, CancellationToken.None);
        await openMissing.Should().ThrowAsync<FileNotFoundException>();

        var emptyId = Guid.NewGuid();
        var emptyDescriptor = await storage.SaveAsync(emptyId, new MemoryStream([]), CancellationToken.None);
        emptyDescriptor.WrittenLength.Should().Be(0);
        await using (var empty = await storage.OpenReadAsync(emptyDescriptor.BlobKey, CancellationToken.None))
        {
            empty.Length.Should().Be(0);
            (await empty.ReadAsync(new Byte[1])).Should().Be(0);
        }

        (await storage.DeleteIfExistsAsync(emptyDescriptor.BlobKey, CancellationToken.None)).Should().BeTrue();
    }
}
