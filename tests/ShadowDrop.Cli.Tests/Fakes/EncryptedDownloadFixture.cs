// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Fakes;

using ShadowDrop.Contracts;
using ShadowDrop.Crypto;
using System.Net;
using System.Text;
using System.Text.Json;

/// <summary>
/// Produces a manifest and a CLI-format encrypted download response for a single file, so interactive and
/// non-interactive download flows can be exercised end to end without a live server.
/// </summary>
internal sealed class EncryptedDownloadFixture
{
    private EncryptedDownloadFixture(Guid fileId, Byte[] keyMaterial, Byte[] kdfSalt, Byte[] plaintext, Int32 chunkSize, Byte[][] encryptedChunks)
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

    public String FileName => "payload.bin";

    public Byte[] KdfSalt { get; }

    public Byte[] Plaintext { get; }

    public String ShareKey => Convert.ToHexStringLower(KeyMaterial);

    private Byte[] KeyMaterial { get; }

    public static EncryptedDownloadFixture Create()
    {
        var fileId = Guid.NewGuid();
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

    public HttpResponseMessage CreateDownloadResponse()
    {
        var responseBody = EncryptedChunks.SelectMany(static chunk => chunk).ToArray();
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(responseBody)
        };
        response.Content.Headers.ContentType = new(DownloadHeaderConstants.CliDownloadContentType);
        response.Content.Headers.ContentLength = responseBody.LongLength;
        response.Headers.TryAddWithoutValidation(DownloadHeaderConstants.FirstChunkIndexHeaderName, "0");
        response.Headers.TryAddWithoutValidation(DownloadHeaderConstants.LastChunkIndexHeaderName, (EncryptedChunks.Length - 1).ToString());
        response.Headers.TryAddWithoutValidation(DownloadHeaderConstants.PlaintextRangeStartHeaderName, "0");
        response.Headers.TryAddWithoutValidation(DownloadHeaderConstants.PlaintextRangeEndHeaderName, Plaintext.LongLength.ToString());
        response.Headers.TryAddWithoutValidation(DownloadHeaderConstants.TotalPlaintextSizeHeaderName, Plaintext.LongLength.ToString());
        response.Headers.TryAddWithoutValidation(DownloadHeaderConstants.ChunkSizeHeaderName, ChunkSize.ToString());
        response.Headers.TryAddWithoutValidation(DownloadHeaderConstants.FinalChunkPlaintextLengthHeaderName, ChunkSize.ToString());
        return response;
    }

    public ShareManifestFileContract CreateManifestFile(String? fileName = null) =>
        new()
        {
            AlgorithmId = "aes-256-gcm",
            ChunkCount = EncryptedChunks.Length,
            ChunkSize = ChunkSize,
            EncryptionFormatVersion = "1",
            FileId = FileId.ToString(),
            FileName = fileName ?? FileName,
            KdfSalt = Convert.ToBase64String(KdfSalt),
            Length = Plaintext.LongLength,
            PlaintextSha256 = null
        };

    public HttpResponseMessage CreateManifestResponse(params ShareManifestFileContract[] files)
    {
        var manifest = new ShareManifestContract
        {
            Files = files.Length == 0 ? [CreateManifestFile()] : files
        };
        return new(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(manifest), Encoding.UTF8, "application/json")
        };
    }
}
