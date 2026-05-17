// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Downloads;

using FluentAssertions;
using NUnit.Framework;
using ShadowDrop.Api.Downloads;
using ShadowDrop.Api.Shares;
using ShadowDrop.Api.Uploads;
using ShadowDrop.Contracts;
using ShadowDrop.Crypto;
using System.Reflection;
using System.Security.Cryptography;

public sealed class DownloadFileServiceTests
{
    [Test]
    public async Task DirectHttpDecryptingStream_ShouldReuseDerivedContentKeyAcrossChunks()
    {
        var fileId = Guid.NewGuid();
        var payload = CreateDirectHttpPayload(fileId);
        var encryptedStream = new TrackingReadStream(payload.Ciphertext);
        var shareSecret = Convert.FromBase64String(payload.KeyMaterialBase64);

        await using var decryptingStream = await CreateDirectHttpDecryptingStreamAsync(encryptedStream,
                                                                                       CreateUploadedFileRecord(fileId, payload),
                                                                                       shareSecret,
                                                                                       CancellationToken.None);
        var initialContentKey = GetPrivateField<ContentKey>(decryptingStream, "_contentKey");

        var firstReadBuffer = new Byte[payload.ChunkSize - 5];
        var secondReadBuffer = new Byte[payload.ChunkSize];
        var firstRead = await decryptingStream.ReadAsync(firstReadBuffer, CancellationToken.None);
        var secondRead = await decryptingStream.ReadAsync(secondReadBuffer, CancellationToken.None);

        firstRead.Should().Be(firstReadBuffer.Length);
        secondRead.Should().Be(secondReadBuffer.Length);
        GetPrivateField<ContentKey>(decryptingStream, "_contentKey").Should().BeSameAs(initialContentKey);
        GetContentKeyBytes(initialContentKey).Should().Contain(value => value != 0);
    }

    [Test]
    public async Task DirectHttpDecryptingStreamCreateAsync_ShouldZeroShareSecretWhenInitializationFails()
    {
        var fileId = Guid.NewGuid();
        var payload = CreateDirectHttpPayload(fileId);
        var encryptedStream = new TrackingReadStream(payload.Ciphertext);
        var uploadedFile = new UploadedFileRecord(fileId,
                                                  "blob-key",
                                                  "cipher.bin",
                                                  payload.Plaintext.LongLength,
                                                  payload.Ciphertext.LongLength,
                                                  "application/octet-stream",
                                                  FormatConstants.EncryptionFormatVersion,
                                                  FormatConstants.Aes256GcmAlgorithmId,
                                                  payload.ChunkSize,
                                                  payload.ChunkCount,
                                                  payload.KdfSaltBase64,
                                                  payload.PlaintextSha256);
        var wrongShareSecret = Enumerable.Repeat((Byte)42, 32).ToArray();

        var act = async () => await CreateDirectHttpDecryptingStreamAsync(encryptedStream,
                                                                          uploadedFile,
                                                                          wrongShareSecret,
                                                                          CancellationToken.None);

        await act.Should().ThrowAsync<CryptographicException>();
        wrongShareSecret.Should().OnlyContain(value => value == 0);
        encryptedStream.DisposeCount.Should().Be(1);
        encryptedStream.WasDisposed.Should().BeTrue();
    }

    [Test]
    public async Task DirectHttpDecryptingStreamDisposeAsync_ShouldZeroRetainedContentKeyAndShareSecret()
    {
        var fileId = Guid.NewGuid();
        var payload = CreateDirectHttpPayload(fileId);
        var encryptedStream = new TrackingReadStream(payload.Ciphertext);
        var uploadedFile = new UploadedFileRecord(fileId,
                                                  "blob-key",
                                                  "cipher.bin",
                                                  payload.Plaintext.LongLength,
                                                  payload.Ciphertext.LongLength,
                                                  "application/octet-stream",
                                                  FormatConstants.EncryptionFormatVersion,
                                                  FormatConstants.Aes256GcmAlgorithmId,
                                                  payload.ChunkSize,
                                                  payload.ChunkCount,
                                                  payload.KdfSaltBase64,
                                                  payload.PlaintextSha256);
        var shareSecret = Convert.FromBase64String(payload.KeyMaterialBase64);

        var stream = await CreateDirectHttpDecryptingStreamAsync(encryptedStream,
                                                                 uploadedFile,
                                                                 shareSecret,
                                                                 CancellationToken.None);
        var contentKey = GetPrivateField<ContentKey>(stream, "_contentKey");
        var keyMaterial = GetContentKeyBytes(contentKey);

        keyMaterial.Should().Contain(value => value != 0);

        await stream.DisposeAsync();

        shareSecret.Should().OnlyContain(value => value == 0);
        keyMaterial.Should().OnlyContain(value => value == 0);
        encryptedStream.DisposeCount.Should().Be(1);
        encryptedStream.WasDisposed.Should().BeTrue();
    }

    [Test]
    public async Task ResolveAsync_ShouldDecryptDirectHttpContentWithoutBufferingFullPlaintext()
    {
        var fileId = Guid.NewGuid();
        var shareToken = "direct-http-share-token";
        var payload = CreateDirectHttpPayload(fileId);
        var encryptedStream = new TrackingReadStream(payload.Ciphertext);
        var shareRepository = new StubShareMetadataRepository(
            new(Guid.NewGuid(),
                TokenHashing.ComputeHashBase64(shareToken),
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow.AddHours(1),
                null,
                ShareCleanupState.Pending,
                true,
                null,
                [new(fileId, "cipher.bin", "renamed.bin")]));
        var uploadedFileRepository = new StubUploadedFileMetadataRepository(
            new(fileId,
                "blob-key",
                "cipher.bin",
                payload.Plaintext.LongLength,
                payload.Ciphertext.LongLength,
                "application/octet-stream",
                FormatConstants.EncryptionFormatVersion,
                FormatConstants.Aes256GcmAlgorithmId,
                payload.ChunkSize,
                payload.ChunkCount,
                payload.KdfSaltBase64,
                payload.PlaintextSha256));
        var blobStorage = new StubBlobStorage(encryptedStream);
        var sut = new DownloadFileService(shareRepository, uploadedFileRepository, blobStorage, TimeProvider.System);

        var result = await sut.ResolveAsync(shareToken,
                                            fileId,
                                            null,
                                            payload.KeyMaterialBase64,
                                            null,
                                            CancellationToken.None);

        result.Status.Should().Be(DownloadLookupStatus.Success);
        result.Resolution.Should().NotBeNull();
        result.Resolution!.ContentStream.Should().NotBeOfType<MemoryStream>();
        encryptedStream.Position.Should().Be(payload.ChunkSize + 16);

        var buffer = new Byte[19];
        await using var plaintextStream = result.Resolution.ContentStream;
        using var plaintext = new MemoryStream();
        while (true)
        {
            var bytesRead = await plaintextStream.ReadAsync(buffer, CancellationToken.None);
            if (bytesRead == 0)
            {
                break;
            }

            await plaintext.WriteAsync(buffer.AsMemory(0, bytesRead), CancellationToken.None);
        }

        plaintext.ToArray().Should().Equal(payload.Plaintext);
        encryptedStream.Position.Should().Be(payload.Ciphertext.LongLength);
        await plaintextStream.DisposeAsync();
        encryptedStream.DisposeCount.Should().Be(1);
        encryptedStream.WasDisposed.Should().BeTrue();
    }

    [Test]
    public async Task ResolveAsync_ShouldDisposeEncryptedStreamWhenDirectHttpStreamCreationFails()
    {
        var fileId = Guid.NewGuid();
        var shareToken = "direct-http-share-token";
        var payload = CreateDirectHttpPayload(fileId);
        var encryptedStream = new TrackingReadStream(payload.Ciphertext);
        var shareRepository = new StubShareMetadataRepository(
            new(Guid.NewGuid(),
                TokenHashing.ComputeHashBase64(shareToken),
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow.AddHours(1),
                null,
                ShareCleanupState.Pending,
                true,
                null,
                [new(fileId, "cipher.bin", "renamed.bin")]));
        var uploadedFileRepository = new StubUploadedFileMetadataRepository(
            new(fileId,
                "blob-key",
                "cipher.bin",
                payload.Plaintext.LongLength,
                payload.Ciphertext.LongLength,
                "application/octet-stream",
                FormatConstants.EncryptionFormatVersion,
                FormatConstants.Aes256GcmAlgorithmId,
                payload.ChunkSize,
                payload.ChunkCount,
                payload.KdfSaltBase64,
                payload.PlaintextSha256));
        var blobStorage = new StubBlobStorage(encryptedStream);
        var sut = new DownloadFileService(shareRepository, uploadedFileRepository, blobStorage, TimeProvider.System);

        var result = await sut.ResolveAsync(shareToken,
                                            fileId,
                                            null,
                                            Convert.ToBase64String(Enumerable.Repeat((Byte)42, 32).ToArray()),
                                            null,
                                            CancellationToken.None);

        result.Status.Should().Be(DownloadLookupStatus.InvalidRequest);
        result.Resolution.Should().BeNull();
        encryptedStream.DisposeCount.Should().Be(1);
        encryptedStream.WasDisposed.Should().BeTrue();
    }

    [Test]
    public async Task ResolveAsync_ShouldReturnNotFound_WhenCliDecryptMetadataExistsButBlobIsMissing()
    {
        var fileId = Guid.NewGuid();
        var shareToken = "cli-share-token";
        var shareRepository = new StubShareMetadataRepository(
            new(Guid.NewGuid(),
                TokenHashing.ComputeHashBase64(shareToken),
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow.AddHours(1),
                null,
                ShareCleanupState.Pending,
                false,
                null,
                [new(fileId, "cipher.bin", "renamed.bin")]));
        var uploadedFileRepository = new StubUploadedFileMetadataRepository(
            new(fileId,
                "blob-key",
                "cipher.bin",
                128,
                160,
                "application/octet-stream",
                FormatConstants.EncryptionFormatVersion,
                FormatConstants.Aes256GcmAlgorithmId,
                64,
                2,
                Convert.ToBase64String(Enumerable.Range(0, 32).Select(static value => (Byte)value).ToArray()),
                new('a', 64)));
        var blobStorage = new MissingBlobStorage();
        var sut = new DownloadFileService(shareRepository, uploadedFileRepository, blobStorage, TimeProvider.System);

        var result = await sut.ResolveAsync(shareToken,
                                            fileId,
                                            null,
                                            null,
                                            null,
                                            CancellationToken.None);

        result.Status.Should().Be(DownloadLookupStatus.NotFound);
        result.Resolution.Should().BeNull();
    }

    [Test]
    public async Task ResolveAsync_ShouldReturnNotFound_WhenDirectHttpMetadataExistsButBlobIsMissing()
    {
        var fileId = Guid.NewGuid();
        var shareToken = "direct-http-share-token";
        var payload = CreateDirectHttpPayload(fileId);
        var shareRepository = new StubShareMetadataRepository(
            new(Guid.NewGuid(),
                TokenHashing.ComputeHashBase64(shareToken),
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow.AddHours(1),
                null,
                ShareCleanupState.Pending,
                true,
                null,
                [new(fileId, "cipher.bin", "renamed.bin")]));
        var uploadedFileRepository = new StubUploadedFileMetadataRepository(CreateUploadedFileRecord(fileId, payload));
        var blobStorage = new MissingBlobStorage();
        var sut = new DownloadFileService(shareRepository, uploadedFileRepository, blobStorage, TimeProvider.System);

        var result = await sut.ResolveAsync(shareToken,
                                            fileId,
                                            null,
                                            payload.KeyMaterialBase64,
                                            null,
                                            CancellationToken.None);

        result.Status.Should().Be(DownloadLookupStatus.NotFound);
        result.Resolution.Should().BeNull();
    }

    [Test]
    public async Task WithDecodedDirectHttpKeyMaterialAsync_ShouldZeroDecodedBytesWhenFailureOccursBeforeOwnershipTransfer()
    {
        var keyMaterialBase64 = Convert.ToBase64String(Enumerable.Range(1, 32).Select(value => (Byte)value).ToArray());
        Byte[]? capturedDecodedBytes = null;

        var act = async () => await DownloadFileService.WithDecodedDirectHttpKeyMaterialAsync<Int32>(
            keyMaterialBase64,
            secretBytes =>
            {
                capturedDecodedBytes = secretBytes;
                throw new CryptographicException("Blob open failed before ownership transfer.");
            });

        await act.Should().ThrowAsync<CryptographicException>();
        capturedDecodedBytes.Should().NotBeNull();
        capturedDecodedBytes!.Should().OnlyContain(value => value == 0);
    }

    private static async Task<Stream> CreateDirectHttpDecryptingStreamAsync(Stream encryptedContent,
                                                                            UploadedFileRecord uploadedFile,
                                                                            Byte[] shareSecret,
                                                                            CancellationToken cancellationToken)
    {
        var directHttpDecryptingStreamType = typeof(DownloadFileService)
            .GetNestedType("DirectHttpDecryptingStream", BindingFlags.NonPublic);
        directHttpDecryptingStreamType.Should().NotBeNull();

        var createAsyncMethod = directHttpDecryptingStreamType!
            .GetMethod("CreateAsync", BindingFlags.Public | BindingFlags.Static);
        createAsyncMethod.Should().NotBeNull();

        var createTask = (Task)createAsyncMethod!.Invoke(null,
                                                         [encryptedContent, uploadedFile, shareSecret, cancellationToken])!;
        await createTask;

        return (Stream)createTask.GetType().GetProperty(nameof(Task<>.Result))!.GetValue(createTask)!;
    }

    private static DirectHttpPayload CreateDirectHttpPayload(Guid fileId)
    {
        var plaintext = Enumerable.Range(0, 160).Select(value => (Byte)(255 - value)).ToArray();
        var keyMaterial = Enumerable.Range(1, 32).Select(value => (Byte)value).ToArray();
        var kdfSalt = Enumerable.Range(129, 32).Select(value => (Byte)value).ToArray();
        using var shareSecret = ShareSecret.FromBytes(keyMaterial);
        var context = new FileEncryptionContext(fileId, kdfSalt);
        using var contentKey = ChunkEncryptionService.DeriveContentKey(shareSecret, context);
        using var ciphertext = new MemoryStream();
        const Int32 chunkSize = 64;
        var chunkCount = (plaintext.LongLength + chunkSize - 1) / chunkSize;

        for (var chunkIndex = 0L; chunkIndex < chunkCount; chunkIndex++)
        {
            var chunkPlaintext = plaintext.Skip((Int32)(chunkIndex * chunkSize)).Take(chunkSize).ToArray();
            var metadata = new ChunkMetadata(CryptoVersion.V1,
                                             CryptoAlgorithm.Aes256Gcm,
                                             fileId,
                                             chunkSize,
                                             chunkIndex,
                                             chunkPlaintext.Length);
            var encryptedChunk = ChunkEncryptionService.EncryptChunk(chunkPlaintext, contentKey, metadata);
            ciphertext.Write(encryptedChunk.Ciphertext);
        }

        return new(ciphertext.ToArray(),
                   plaintext,
                   chunkSize,
                   chunkCount,
                   Convert.ToBase64String(kdfSalt),
                   Convert.ToBase64String(keyMaterial),
                   Convert.ToHexStringLower(SHA256.HashData(plaintext)));
    }


    private static UploadedFileRecord CreateUploadedFileRecord(Guid fileId, DirectHttpPayload payload) =>
        new(fileId,
            "blob-key",
            "cipher.bin",
            payload.Plaintext.LongLength,
            payload.Ciphertext.LongLength,
            "application/octet-stream",
            FormatConstants.EncryptionFormatVersion,
            FormatConstants.Aes256GcmAlgorithmId,
            payload.ChunkSize,
            payload.ChunkCount,
            payload.KdfSaltBase64,
            payload.PlaintextSha256);

    private static Byte[] GetContentKeyBytes(ContentKey contentKey) => GetPrivateField<Byte[]>(contentKey, "_keyMaterial");

    private static T GetPrivateField<T>(Object instance, String fieldName) where T : class
    {
        var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull($"Expected private field '{fieldName}' on {instance.GetType().FullName}.");
        var value = field!.GetValue(instance);
        value.Should().BeOfType<T>();
        return (T)value!;
    }

    private sealed record DirectHttpPayload(
        Byte[] Ciphertext,
        Byte[] Plaintext,
        Int32 ChunkSize,
        Int64 ChunkCount,
        String KdfSaltBase64,
        String KeyMaterialBase64,
        String PlaintextSha256);

    private sealed class MissingBlobStorage : IBlobStorage
    {
        public Task DeleteIfExistsAsync(String blobKey, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<Stream> OpenReadAsync(String blobKey, CancellationToken cancellationToken) =>
            Task.FromException<Stream>(new FileNotFoundException("Blob file missing.", blobKey));

        public Task<UploadBlobDescriptor> SaveAsync(Guid fileId, Stream encryptedContent, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class StubBlobStorage(TrackingReadStream stream) : IBlobStorage
    {
        public Task DeleteIfExistsAsync(String blobKey, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<Stream> OpenReadAsync(String blobKey, CancellationToken cancellationToken) => Task.FromResult<Stream>(stream);

        public Task<UploadBlobDescriptor> SaveAsync(Guid fileId, Stream encryptedContent, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class StubShareMetadataRepository(ShareRecord record) : IShareMetadataRepository
    {
        public Task CreateAsync(ShareRecord uploadedFileRecord, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ShareRecord?> GetAsync(Guid shareId, CancellationToken cancellationToken) => Task.FromResult<ShareRecord?>(record);

        public Task<ShareRecord?> GetByShareTokenHashAsync(String shareTokenHashBase64, CancellationToken cancellationToken) =>
            Task.FromResult(record.ShareTokenHashBase64 == shareTokenHashBase64 ? record : null);
    }

    private sealed class StubUploadedFileMetadataRepository(UploadedFileRecord record) : IUploadedFileMetadataRepository
    {
        public Task<UploadedFileRecord?> GetAsync(Guid fileId, CancellationToken cancellationToken) =>
            Task.FromResult<UploadedFileRecord?>(record.FileId == fileId ? record : null);

        public Task<Guid> ReserveFileIdAsync(CancellationToken cancellationToken) => Task.FromResult(Guid.NewGuid());

        public Task SaveAsync(UploadedFileRecord uploadedFileRecord, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Boolean> TryCompleteReservationAsync(UploadedFileRecord uploadedFileRecord, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class TrackingReadStream(Byte[] content) : MemoryStream(content, false)
    {
        private Boolean _disposeTracked;

        public Int32 DisposeCount { get; private set; }

        public Boolean WasDisposed { get; private set; }

        public override async ValueTask DisposeAsync()
        {
            TrackDispose();
            await base.DisposeAsync();
        }

        protected override void Dispose(Boolean disposing)
        {
            TrackDispose();
            base.Dispose(disposing);
        }

        private void TrackDispose()
        {
            if (_disposeTracked)
            {
                return;
            }

            _disposeTracked = true;
            DisposeCount++;
            WasDisposed = true;
        }
    }
}
