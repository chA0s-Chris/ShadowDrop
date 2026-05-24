// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Uploads;

using FluentAssertions;
using NUnit.Framework;
using ShadowDrop.Cli.Uploads;
using ShadowDrop.Crypto;

[NonParallelizable]
public sealed class EncryptedFileContentTests
{
    [Test]
    public async Task CopyToAsync_ShouldHonorCancellationToken()
    {
        var rootDirectory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "artifacts", "encrypted-file-content-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(rootDirectory);

        try
        {
            var fileId = Guid.NewGuid();
            var filePath = Path.Combine(rootDirectory, "payload.bin");
            File.WriteAllBytes(filePath, Enumerable.Range(0, 512).Select(static value => (Byte)value).ToArray());
            var kdfSalt = FileEncryptionContext.GenerateKdfSalt();
            using var shareSecret = ShareSecret.Generate();
            using var content = new EncryptedFileContent(new(filePath),
                                                         shareSecret,
                                                         new(fileId, kdfSalt),
                                                         128,
                                                         new FileInfo(filePath).Length + (4 * 16),
                                                         new CancellationToken(true));
            using var sink = new MemoryStream();

            Func<Task> act = async () => await content.CopyToAsync(sink, null, CancellationToken.None);

            await act.Should().ThrowAsync<OperationCanceledException>();
        }
        finally
        {
            if (Directory.Exists(rootDirectory))
            {
                Directory.Delete(rootDirectory, true);
            }
        }
    }

    [Test]
    public void ZeroPlaintextBuffer_ShouldOverwriteBufferContents()
    {
        var buffer = Enumerable.Range(1, 32).Select(static value => (Byte)value).ToArray();

        EncryptedFileContent.ZeroPlaintextBuffer(buffer);

        buffer.Should().OnlyContain(static value => value == 0);
    }
}
