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
using System.Text;
using System.Text.Json;

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
    public async Task DirectHttpDecryptingStreamDispose_ShouldZeroRetainedContentKeyAndShareSecret()
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

        stream.Dispose();

        shareSecret.Should().OnlyContain(value => value == 0);
        keyMaterial.Should().OnlyContain(value => value == 0);
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
                                            null,
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
                                            null,
                                            null,
                                            CancellationToken.None);

        result.Status.Should().Be(DownloadLookupStatus.InvalidRequest);
        result.Resolution.Should().BeNull();
        encryptedStream.DisposeCount.Should().Be(1);
        encryptedStream.WasDisposed.Should().BeTrue();
    }

    [Test]
    public async Task ResolveAsync_ShouldReturnCliResumableContract_FromSharedSerializerContext_ForCliDecryptMode()
    {
        var fileId = Guid.NewGuid();
        var shareToken = "cli-share-token";
        var payload = CreateDirectHttpPayload(fileId);
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
        var uploadedFileRepository = new StubUploadedFileMetadataRepository(CreateUploadedFileRecord(fileId, payload));
        var blobStorage = new StubBlobStorage(new TrackingReadStream(payload.Ciphertext));
        var sut = new DownloadFileService(shareRepository, uploadedFileRepository, blobStorage, TimeProvider.System);

        var result = await sut.ResolveAsync(shareToken,
                                            fileId,
                                            null,
                                            null,
                                            null,
                                            64,
                                            120,
                                            CancellationToken.None);

        result.Status.Should().Be(DownloadLookupStatus.Success);
        result.Resolution.Should().NotBeNull();
        result.Resolution!.ResponseContentType.Should().Be("application/json");
        result.Resolution.FileContentType.Should().Be("application/octet-stream");
        result.Resolution.ContentStream.Should().NotBeOfType<MemoryStream>();

        using var content = new MemoryStream();
        await result.Resolution.ContentStream.CopyToAsync(content, CancellationToken.None);
        var json = content.ToArray();
        var contract = JsonSerializer.Deserialize(json, ContractsJsonSerializerContext.Default.CliResumableDownloadContract);

        contract.Should().NotBeNull();
        contract!.FirstChunkIndex.Should().Be(1);
        contract.LastChunkIndex.Should().Be(1);
        contract.RequestedRange.Should().BeEquivalentTo(new RequestedPlaintextRangeContract
        {
            Start = 64,
            End = 120
        });
        Convert.FromBase64String(contract.EncryptedPayload!).Should().Equal(payload.Ciphertext.Skip(80).Take(80));
        JsonSerializer.Serialize(contract, ContractsJsonSerializerContext.Default.CliResumableDownloadContract)
                      .Should()
                      .Contain("\"encryptedPayload\":\"");
    }

    [Test]
    public async Task ResolveAsync_ShouldReturnForbidden_WhenBearerTokenIsExpired()
    {
        var fileId = Guid.NewGuid();
        var shareToken = "valid-share-token";
        var bearerToken = "valid-bearer-token-12345";
        var now = DateTimeOffset.UtcNow;
        var shareRepository = new StubShareMetadataRepository(
            new(Guid.NewGuid(),
                TokenHashing.ComputeHashBase64(shareToken),
                now,
                now.AddHours(1),
                null,
                ShareCleanupState.Pending,
                false,
                new(TokenHashing.ComputeHashBase64(bearerToken), now.AddMinutes(-5)),
                [new(fileId, "file.bin", null)]));
        var uploadedFileRepository = new StubUploadedFileMetadataRepository(
            new(fileId,
                "blob-key",
                "file.bin",
                128,
                160,
                "application/octet-stream",
                FormatConstants.EncryptionFormatVersion,
                FormatConstants.Aes256GcmAlgorithmId,
                64,
                2,
                Convert.ToBase64String(Enumerable.Range(0, 32).Select(static value => (Byte)value).ToArray()),
                new('a', 64)));
        var blobStorage = new StubBlobStorage(new MemoryStream([], false));
        var sut = new DownloadFileService(shareRepository, uploadedFileRepository, blobStorage, TimeProvider.System);

        var result = await sut.ResolveAsync(shareToken,
                                            fileId,
                                            bearerToken,
                                            null,
                                            null,
                                            null,
                                            null,
                                            CancellationToken.None);

        result.Status.Should().Be(DownloadLookupStatus.Forbidden);
        result.Resolution.Should().BeNull();
    }

    [Test]
    public async Task ResolveAsync_ShouldReturnForbidden_WhenBearerTokenIsWrong()
    {
        var fileId = Guid.NewGuid();
        var shareToken = "valid-share-token";
        var correctBearerToken = "correct-bearer-token-12345";
        var wrongBearerToken = "wrong-bearer-token-67890";
        var shareRepository = new StubShareMetadataRepository(
            new(Guid.NewGuid(),
                TokenHashing.ComputeHashBase64(shareToken),
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow.AddHours(1),
                null,
                ShareCleanupState.Pending,
                false,
                new(TokenHashing.ComputeHashBase64(correctBearerToken), DateTimeOffset.UtcNow.AddHours(1)),
                [new(fileId, "file.bin", null)]));
        var uploadedFileRepository = new StubUploadedFileMetadataRepository(
            new(fileId,
                "blob-key",
                "file.bin",
                128,
                160,
                "application/octet-stream",
                FormatConstants.EncryptionFormatVersion,
                FormatConstants.Aes256GcmAlgorithmId,
                64,
                2,
                Convert.ToBase64String(Enumerable.Range(0, 32).Select(static value => (Byte)value).ToArray()),
                new('a', 64)));
        var blobStorage = new StubBlobStorage(new MemoryStream([], false));
        var sut = new DownloadFileService(shareRepository, uploadedFileRepository, blobStorage, TimeProvider.System);

        var result = await sut.ResolveAsync(shareToken,
                                            fileId,
                                            wrongBearerToken,
                                            null,
                                            null,
                                            null,
                                            null,
                                            CancellationToken.None);

        result.Status.Should().Be(DownloadLookupStatus.Forbidden);
        result.Resolution.Should().BeNull();
    }

    [Test]
    public async Task ResolveAsync_ShouldReturnInvalidRequest_WhenCliDecryptRangeIsIncomplete()
    {
        var fileId = Guid.NewGuid();
        var shareToken = "cli-share-token";
        var payload = CreateDirectHttpPayload(fileId);
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
        var uploadedFileRepository = new StubUploadedFileMetadataRepository(CreateUploadedFileRecord(fileId, payload));
        var blobStorage = new StubBlobStorage(new TrackingReadStream(payload.Ciphertext));
        var sut = new DownloadFileService(shareRepository, uploadedFileRepository, blobStorage, TimeProvider.System);

        var result = await sut.ResolveAsync(shareToken,
                                            fileId,
                                            null,
                                            null,
                                            null,
                                            64,
                                            null,
                                            CancellationToken.None);

        result.Status.Should().Be(DownloadLookupStatus.InvalidRequest);
        result.Resolution.Should().BeNull();
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
                                            null,
                                            null,
                                            CancellationToken.None);

        result.Status.Should().Be(DownloadLookupStatus.NotFound);
        result.Resolution.Should().BeNull();
    }

    [Test]
    public async Task ResolveAsync_ShouldStreamCliDecryptContract_ForLargeRangesWithoutPrebufferingEncryptedPayload()
    {
        var fileId = Guid.NewGuid();
        var shareToken = "cli-share-token";
        const Int32 chunkSize = 4096;
        const Int64 chunkCount = 700_000;
        var plaintextLength = chunkSize * chunkCount;
        var ciphertextLength = plaintextLength + (chunkCount * 16);
        var shareRepository = new StubShareMetadataRepository(
            new(Guid.NewGuid(),
                TokenHashing.ComputeHashBase64(shareToken),
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow.AddHours(1),
                null,
                ShareCleanupState.Pending,
                false,
                null,
                [new(fileId, "cipher.bin", "large.bin")]));
        var uploadedFileRepository = new StubUploadedFileMetadataRepository(
            new(fileId,
                "blob-key",
                "cipher.bin",
                plaintextLength,
                ciphertextLength,
                "application/octet-stream",
                FormatConstants.EncryptionFormatVersion,
                FormatConstants.Aes256GcmAlgorithmId,
                chunkSize,
                chunkCount,
                Convert.ToBase64String(Enumerable.Range(0, 32).Select(static value => (Byte)(value + 1)).ToArray()),
                new('a', 64)));
        var encryptedStream = new ZeroGeneratingReadStream(ciphertextLength);
        var blobStorage = new StubBlobStorage(encryptedStream);
        var sut = new DownloadFileService(shareRepository, uploadedFileRepository, blobStorage, TimeProvider.System);

        var result = await sut.ResolveAsync(shareToken,
                                            fileId,
                                            null,
                                            null,
                                            null,
                                            0,
                                            plaintextLength,
                                            CancellationToken.None);

        result.Status.Should().Be(DownloadLookupStatus.Success);
        result.Resolution.Should().NotBeNull();
        result.Resolution!.ContentStream.Should().NotBeOfType<MemoryStream>();
        result.Resolution.ResponseContentLength.Should().BeGreaterThan(Int32.MaxValue);
        encryptedStream.TotalBytesRead.Should().Be(0);

        var prefixBuffer = new Byte[64];
        var bytesRead = await result.Resolution.ContentStream.ReadAsync(prefixBuffer, CancellationToken.None);

        bytesRead.Should().BeGreaterThan(0);
        Encoding.UTF8.GetString(prefixBuffer, 0, bytesRead).Should().StartWith("{\"firstChunkIndex\":0,");
        encryptedStream.TotalBytesRead.Should().Be(0);

        await result.Resolution.ContentStream.DisposeAsync();
        encryptedStream.WasDisposed.Should().BeTrue();
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
                return Task.FromException<(Int32 Result, Boolean OwnershipTransferred)>(
                    new CryptographicException("Blob open failed before ownership transfer."));
            });

        await act.Should().ThrowAsync<CryptographicException>();
        capturedDecodedBytes.Should().NotBeNull();
        capturedDecodedBytes!.Should().OnlyContain(value => value == 0);
    }

    [Test]
    public async Task WithDecodedDirectHttpKeyMaterialAsync_ShouldZeroDecodedBytesWhenResultDoesNotTakeOwnership()
    {
        var keyMaterialBase64 = Convert.ToBase64String(Enumerable.Range(1, 32).Select(value => (Byte)value).ToArray());
        Byte[]? capturedDecodedBytes = null;

        var result = await DownloadFileService.WithDecodedDirectHttpKeyMaterialAsync(
            keyMaterialBase64,
            secretBytes =>
            {
                capturedDecodedBytes = secretBytes;
                return Task.FromResult((7, false));
            });

        result.Should().Be(7);
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
            .GetMethod("CreateAsync",
                       BindingFlags.Public | BindingFlags.Static,
                       null,
                       [typeof(Stream), typeof(UploadedFileRecord), typeof(Byte[]), typeof(CancellationToken)],
                       null);
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

    private sealed class StubBlobStorage(Stream stream) : IBlobStorage
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

        public Task ReleaseClaimAsync(Guid fileId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Guid> ReserveFileIdAsync(CancellationToken cancellationToken) => Task.FromResult(Guid.NewGuid());

        public Task<Boolean> TryClaimReservationAsync(Guid fileId, CancellationToken cancellationToken) => throw new NotSupportedException();

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

    private sealed class ZeroGeneratingReadStream(Int64 length) : Stream
    {
        private readonly Int64 _length = length;
        private Boolean _disposed;

        public override Boolean CanRead => !_disposed;
        public override Boolean CanSeek => true;
        public override Boolean CanWrite => false;
        public override Int64 Length => _length;
        public override Int64 Position { get; set; }

        public Int64 TotalBytesRead { get; private set; }
        public Boolean WasDisposed { get; private set; }

        public override ValueTask DisposeAsync()
        {
            WasDisposed = true;
            _disposed = true;
            return base.DisposeAsync();
        }

        public override void Flush() => throw new NotSupportedException();

        public override Int32 Read(Byte[] buffer, Int32 offset, Int32 count) =>
            ReadAsync(buffer.AsMemory(offset, count), CancellationToken.None).AsTask().GetAwaiter().GetResult();

        public override Int32 Read(Span<Byte> buffer)
        {
            var bytesToRead = (Int32)Math.Min(buffer.Length, _length - Position);
            if (bytesToRead <= 0)
            {
                return 0;
            }

            buffer[..bytesToRead].Clear();
            Position += bytesToRead;
            TotalBytesRead += bytesToRead;
            return bytesToRead;
        }

        public override Task<Int32> ReadAsync(Byte[] buffer, Int32 offset, Int32 count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override ValueTask<Int32> ReadAsync(Memory<Byte> buffer, CancellationToken cancellationToken = default)
        {
            var bytesToRead = (Int32)Math.Min(buffer.Length, _length - Position);
            if (bytesToRead <= 0)
            {
                return ValueTask.FromResult(0);
            }

            buffer.Span[..bytesToRead].Clear();
            Position += bytesToRead;
            TotalBytesRead += bytesToRead;
            return ValueTask.FromResult(bytesToRead);
        }

        public override Int64 Seek(Int64 offset, SeekOrigin origin) =>
            Position = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => Position + offset,
                SeekOrigin.End => _length + offset,
                _ => throw new ArgumentOutOfRangeException(nameof(origin))
            };

        public override void SetLength(Int64 value) => throw new NotSupportedException();
        public override void Write(Byte[] buffer, Int32 offset, Int32 count) => throw new NotSupportedException();

        protected override void Dispose(Boolean disposing)
        {
            WasDisposed = true;
            _disposed = true;
            base.Dispose(disposing);
        }
    }
}
