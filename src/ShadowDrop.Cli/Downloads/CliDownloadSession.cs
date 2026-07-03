// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Downloads;

using ShadowDrop.Contracts;
using ShadowDrop.Crypto;

/// <summary>
/// Downloads, decrypts, and resumes streamed CLI download responses.
/// </summary>
public sealed class CliDownloadSession : IDisposable
{
    private const Int32 AesGcmTagLength = 16;
    private readonly String? _bearerToken;

    private readonly ContentKey _contentKey;
    private readonly Stream _destination;
    private readonly Uri _downloadUri;
    private readonly FileEncryptionContext _encryptionContext;
    private readonly HttpClient _httpClient;
    private readonly IProgress<Int64>? _progress;
    private Boolean _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="CliDownloadSession"/> class.
    /// </summary>
    /// <param name="httpClient">The HTTP client used to issue download requests.</param>
    /// <param name="downloadUri">The public download URI.</param>
    /// <param name="destination">The destination stream for durable plaintext bytes.</param>
    /// <param name="shareSecret">The share secret used to derive the file-scoped content key.</param>
    /// <param name="encryptionContext">The file encryption context.</param>
    /// <param name="bearerToken">The optional bearer token sent with each download request.</param>
    /// <param name="durablePlaintextLength">The already-persisted plaintext byte count.</param>
    /// <param name="totalPlaintextSize">The total plaintext file size when it is already known.</param>
    /// <param name="progress">An optional sink that receives the cumulative durable plaintext byte count after each chunk is written.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="httpClient"/>, <paramref name="downloadUri"/>, <paramref name="destination"/>,
    /// <paramref name="shareSecret"/>, or <paramref name="encryptionContext"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="durablePlaintextLength"/> is negative.</exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="destination"/> is not writable, or when a resumed download is requested with a non-seekable destination.
    /// </exception>
    public CliDownloadSession(HttpClient httpClient,
                              Uri downloadUri,
                              Stream destination,
                              ShareSecret shareSecret,
                              FileEncryptionContext encryptionContext,
                              String? bearerToken = null,
                              Int64 durablePlaintextLength = 0,
                              Int64? totalPlaintextSize = null,
                              IProgress<Int64>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(downloadUri);
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(shareSecret);
        ArgumentNullException.ThrowIfNull(encryptionContext);
        ArgumentOutOfRangeException.ThrowIfNegative(durablePlaintextLength);
        if (totalPlaintextSize is not null)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(totalPlaintextSize.Value);
            if (durablePlaintextLength > totalPlaintextSize.Value)
            {
                throw new ArgumentOutOfRangeException(nameof(durablePlaintextLength), "The durable plaintext length must not exceed the total size.");
            }
        }

        if (!destination.CanWrite)
        {
            throw new ArgumentException("The destination stream must be writable.", nameof(destination));
        }

        if (durablePlaintextLength > 0 && !destination.CanSeek)
        {
            throw new ArgumentException("A resumed download requires a seekable destination stream.", nameof(destination));
        }

        _httpClient = httpClient;
        _downloadUri = downloadUri;
        _destination = destination;
        _encryptionContext = encryptionContext;
        _bearerToken = bearerToken;
        _progress = progress;
        _contentKey = ChunkEncryptionService.DeriveContentKey(shareSecret, encryptionContext);
        DurablePlaintextLength = durablePlaintextLength;
        TotalPlaintextSize = totalPlaintextSize;
        // Report the starting offset so resumed downloads begin progress from the already-persisted byte count.
        progress?.Report(durablePlaintextLength);
    }

    /// <summary>
    /// Gets the last durable plaintext byte offset, expressed as the next plaintext byte to request.
    /// </summary>
    public Int64 DurablePlaintextLength { get; private set; }

    /// <summary>
    /// Gets the total plaintext file size when it is known.
    /// </summary>
    public Int64? TotalPlaintextSize { get; private set; }

    /// <summary>
    /// Downloads and decrypts the remaining plaintext bytes into the destination stream.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the download finishes.</returns>
    /// <exception cref="InvalidOperationException">Thrown when a resume attempt does not yet know the total plaintext size.</exception>
    public async Task DownloadAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (TotalPlaintextSize is not null && DurablePlaintextLength >= TotalPlaintextSize.Value)
        {
            return;
        }

        if (_destination.CanSeek)
        {
            ValidateSeekableDestinationForResume();
            _destination.Position = DurablePlaintextLength;
        }

        var expectedRange = CreateExpectedRange();
        using var request = CliDownloadRequestFactory.CreateGetRequest(_downloadUri, _bearerToken, expectedRange);
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var parsedResponse = CliDownloadResponseParser.Parse(response, expectedRange);
        TotalPlaintextSize = parsedResponse.Metadata.TotalPlaintextSize;

        await using var responseStream = parsedResponse.ContentStream;
        await CopyDecryptedPlaintextAsync(parsedResponse.Metadata, responseStream, cancellationToken);
    }

    private static Int64 GetChunkCount(CliDownloadMetadataContract metadata) =>
        checked(((metadata.TotalPlaintextSize - 1) / metadata.ChunkSize) + 1);

    private static async Task<Byte[]> ReadChunkAsync(Stream encryptedStream, Int32 plaintextChunkLength, CancellationToken cancellationToken)
    {
        var buffer = new Byte[checked(plaintextChunkLength + AesGcmTagLength)];
        var offset = 0;
        while (offset < buffer.Length)
        {
            var bytesRead = await encryptedStream.ReadAsync(buffer.AsMemory(offset), cancellationToken);
            if (bytesRead == 0)
            {
                throw new InvalidDataException("The streamed CLI download response body ended before a complete encrypted chunk was received.");
            }

            offset += bytesRead;
        }

        return buffer;
    }

    private static async Task ValidateStreamExhaustionAsync(Stream encryptedStream, CancellationToken cancellationToken)
    {
        var buffer = new Byte[1];
        var bytesRead = await encryptedStream.ReadAsync(buffer, cancellationToken);
        if (bytesRead > 0)
        {
            throw new InvalidDataException("The streamed CLI download response body contained unexpected trailing data after the final encrypted chunk.");
        }
    }

    private async Task CopyDecryptedPlaintextAsync(CliDownloadMetadataContract metadata, Stream encryptedStream, CancellationToken cancellationToken)
    {
        var chunkCount = GetChunkCount(metadata);
        for (var chunkIndex = metadata.FirstChunkIndex; chunkIndex <= metadata.LastChunkIndex; chunkIndex++)
        {
            var plaintextChunkLength = chunkIndex == chunkCount - 1
                ? metadata.FinalChunkPlaintextLength
                : metadata.ChunkSize;
            var encryptedChunk = await ReadChunkAsync(encryptedStream, plaintextChunkLength, cancellationToken);
            var decryptedChunk = ChunkEncryptionService.DecryptChunk(new(encryptedChunk),
                                                                     _contentKey,
                                                                     new(CryptoVersion.V1,
                                                                         CryptoAlgorithm.Aes256Gcm,
                                                                         _encryptionContext.FileId,
                                                                         metadata.ChunkSize,
                                                                         chunkIndex,
                                                                         plaintextChunkLength,
                                                                         chunkIndex == chunkCount - 1));

            var chunkPlaintextStart = checked(chunkIndex * (Int64)metadata.ChunkSize);
            var chunkPlaintextEnd = checked(chunkPlaintextStart + plaintextChunkLength);
            var writeStart = Math.Max(chunkPlaintextStart, metadata.RequestedRange.Start);
            var writeEnd = Math.Min(chunkPlaintextEnd, metadata.RequestedRange.End);
            if (writeEnd <= writeStart)
            {
                continue;
            }

            var offset = checked((Int32)(writeStart - chunkPlaintextStart));
            var count = checked((Int32)(writeEnd - writeStart));
            await _destination.WriteAsync(decryptedChunk.AsMemory(offset, count), cancellationToken);
            DurablePlaintextLength = checked(DurablePlaintextLength + count);
            _progress?.Report(DurablePlaintextLength);
        }

        await ValidateStreamExhaustionAsync(encryptedStream, cancellationToken);
    }

    private RequestedPlaintextRangeContract? CreateExpectedRange()
    {
        if (DurablePlaintextLength == 0)
        {
            return null;
        }

        if (TotalPlaintextSize is null)
        {
            throw new InvalidOperationException("Cannot resume a streamed CLI download before the total plaintext size is known.");
        }

        if (DurablePlaintextLength >= TotalPlaintextSize.Value)
        {
            return new()
            {
                Start = TotalPlaintextSize.Value,
                End = TotalPlaintextSize.Value
            };
        }

        return new()
        {
            Start = DurablePlaintextLength,
            End = TotalPlaintextSize.Value
        };
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, nameof(CliDownloadSession));

    private void ValidateSeekableDestinationForResume()
    {
        if (DurablePlaintextLength == 0)
        {
            return;
        }

        if (_destination.Length != DurablePlaintextLength)
        {
            throw new InvalidOperationException(
                "Cannot resume a streamed CLI download when the seekable destination length does not match the durable plaintext length.");
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _contentKey.Dispose();
        _disposed = true;
    }
}
