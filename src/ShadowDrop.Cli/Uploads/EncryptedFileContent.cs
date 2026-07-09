// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Uploads;

using ShadowDrop.Crypto;
using System.Net;
using System.Security.Cryptography;

internal sealed class EncryptedFileContent : HttpContent
{
    private readonly CancellationToken _cancellationToken;
    private readonly Int32 _chunkSize;
    private readonly Int64 _encryptedLength;
    private readonly FileEncryptionContext _encryptionContext;
    private readonly FileInfo _file;
    private readonly IProgress<Int64>? _progress;
    private readonly ShareSecret _shareSecret;

    public EncryptedFileContent(FileInfo file,
                                ShareSecret shareSecret,
                                FileEncryptionContext encryptionContext,
                                Int32 chunkSize,
                                Int64 encryptedLength,
                                IProgress<Int64>? progress,
                                CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(shareSecret);
        ArgumentNullException.ThrowIfNull(encryptionContext);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(chunkSize);
        ArgumentOutOfRangeException.ThrowIfNegative(encryptedLength);

        _file = file;
        _shareSecret = shareSecret;
        _encryptionContext = encryptionContext;
        _chunkSize = chunkSize;
        _encryptedLength = encryptedLength;
        _progress = progress;
        _cancellationToken = cancellationToken;
    }

    internal static void ZeroPlaintextBuffer(Byte[] buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        CryptographicOperations.ZeroMemory(buffer);
    }

    protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        => await SerializeToStreamAsync(stream, context, _cancellationToken);

    protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
    {
        using var cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(_cancellationToken, cancellationToken);
        var effectiveCancellationToken = cancellationTokenSource.Token;
        await using var plaintext = _file.OpenRead();
        using var contentKey = ChunkEncryptionService.DeriveContentKey(_shareSecret, _encryptionContext);
        var buffer = new Byte[_chunkSize];
        var chunkIndex = 0L;
        var chunkCount = checked(((_file.Length - 1) / _chunkSize) + 1);
        var encryptedBytesWritten = 0L;

        try
        {
            while (true)
            {
                var bytesRead = await plaintext.ReadAsync(buffer.AsMemory(0, buffer.Length), effectiveCancellationToken);
                if (bytesRead == 0)
                {
                    break;
                }

                var encryptedChunk = ChunkEncryptionService.EncryptChunk(buffer.AsSpan(0, bytesRead),
                                                                         contentKey,
                                                                         new(CryptoVersion.V1,
                                                                             CryptoAlgorithm.Aes256Gcm,
                                                                             _encryptionContext.FileId,
                                                                             _chunkSize,
                                                                             chunkIndex,
                                                                             bytesRead,
                                                                             chunkIndex == chunkCount - 1));
                await stream.WriteAsync(encryptedChunk.CiphertextMemory, effectiveCancellationToken);
                encryptedBytesWritten += encryptedChunk.CiphertextMemory.Length;
                _progress?.Report(encryptedBytesWritten);
                chunkIndex++;
            }
        }
        finally
        {
            ZeroPlaintextBuffer(buffer);
        }
    }

    protected override Boolean TryComputeLength(out Int64 length)
    {
        length = _encryptedLength;
        return true;
    }
}
