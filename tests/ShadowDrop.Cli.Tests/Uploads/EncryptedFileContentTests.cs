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
    [TestCase(384)]
    [TestCase(300)]
    public async Task CopyToAsync_ShouldEncryptOnlyLastChunkAsFinal(Int32 plaintextLength)
    {
        const Int32 chunkSize = 128;
        var rootDirectory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "artifacts", "encrypted-file-content-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(rootDirectory);

        try
        {
            var fileId = Guid.NewGuid();
            var filePath = Path.Combine(rootDirectory, "payload.bin");
            var plaintext = Enumerable.Range(0, plaintextLength).Select(static value => (Byte)value).ToArray();
            File.WriteAllBytes(filePath, plaintext);
            var kdfSalt = FileEncryptionContext.GenerateKdfSalt();
            using var shareSecret = ShareSecret.Generate();
            var chunkCount = ((plaintextLength - 1) / chunkSize) + 1;
            var encryptedLength = plaintextLength + (chunkCount * EncryptedChunk.AuthenticationTagLength);
            using var content = new EncryptedFileContent(new(filePath),
                                                         shareSecret,
                                                         new(fileId, kdfSalt),
                                                         chunkSize,
                                                         encryptedLength,
                                                         null,
                                                         CancellationToken.None);
            using var sink = new MemoryStream();

            await content.CopyToAsync(sink, null, CancellationToken.None);

            var ciphertext = sink.ToArray();
            ciphertext.Length.Should().Be(encryptedLength);
            using var contentKey = ChunkEncryptionService.DeriveContentKey(shareSecret, new(fileId, kdfSalt));
            using var decrypted = new MemoryStream();
            var ciphertextOffset = 0;
            for (var chunkIndex = 0; chunkIndex < chunkCount; chunkIndex++)
            {
                var plaintextChunkLength = Math.Min(chunkSize, plaintextLength - (chunkIndex * chunkSize));
                var encryptedChunkLength = plaintextChunkLength + EncryptedChunk.AuthenticationTagLength;
                // Decrypting binds the expected AAD, so this throws unless the uploader marked exactly the
                // last chunk as final — including a full-sized last chunk when the length is an exact multiple.
                decrypted.Write(ChunkEncryptionService.DecryptChunk(new(ciphertext.AsSpan(ciphertextOffset, encryptedChunkLength).ToArray()),
                                                                    contentKey,
                                                                    new(CryptoVersion.V1,
                                                                        CryptoAlgorithm.Aes256Gcm,
                                                                        fileId,
                                                                        chunkSize,
                                                                        chunkIndex,
                                                                        plaintextChunkLength,
                                                                        chunkIndex == chunkCount - 1)));
                ciphertextOffset += encryptedChunkLength;
            }

            decrypted.ToArray().Should().Equal(plaintext);
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
                                                         null,
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
    public async Task CopyToAsync_ShouldReportActivityAfterEachEncryptedWrite()
    {
        var filePath = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"activity-{Guid.NewGuid():N}.bin");
        await File.WriteAllBytesAsync(filePath, Enumerable.Range(0, 10).Select(static value => (Byte)value).ToArray());
        using var shareSecret = ShareSecret.Generate();
        var context = new FileEncryptionContext(Guid.NewGuid(), FileEncryptionContext.GenerateKdfSalt());
        var activityCount = 0;

        try
        {
            using var content = new EncryptedFileContent(new(filePath), shareSecret, context, 4, 58, null, CancellationToken.None, () => activityCount++);
            await content.CopyToAsync(Stream.Null);

            activityCount.Should().Be(3);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Test]
    public async Task CopyToAsync_ShouldReportCumulativeEncryptedBytesAfterEachChunk()
    {
        const Int32 chunkSize = 128;
        var rootDirectory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "artifacts", "encrypted-file-content-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(rootDirectory);

        try
        {
            var fileId = Guid.NewGuid();
            var filePath = Path.Combine(rootDirectory, "payload.bin");
            File.WriteAllBytes(filePath, Enumerable.Range(0, 300).Select(static value => (Byte)value).ToArray());
            var kdfSalt = FileEncryptionContext.GenerateKdfSalt();
            using var shareSecret = ShareSecret.Generate();
            var encryptedLength = 300 + (3 * EncryptedChunk.AuthenticationTagLength);
            var progress = new CapturingProgress();
            using var content = new EncryptedFileContent(new(filePath),
                                                         shareSecret,
                                                         new(fileId, kdfSalt),
                                                         chunkSize,
                                                         encryptedLength,
                                                         progress,
                                                         CancellationToken.None);
            using var sink = new MemoryStream();

            await content.CopyToAsync(sink, null, CancellationToken.None);

            progress.Values.Should().Equal(144, 288, 348);
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

    private sealed class CapturingProgress : IProgress<Int64>
    {
        private readonly List<Int64> _values = [];

        public IReadOnlyList<Int64> Values => _values;

        public void Report(Int64 value) => _values.Add(value);
    }
}
