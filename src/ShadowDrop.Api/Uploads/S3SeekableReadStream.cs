// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Uploads;

internal sealed class S3SeekableReadStream(IS3Client client, String bucketName, String objectKey, Int64 length) : Stream
{
    private S3ReadResponse? _activeResponse;
    private Boolean _disposed;
    private Int64 _position;

    public override Boolean CanRead => !_disposed;

    public override Boolean CanSeek => !_disposed;

    public override Boolean CanWrite => false;

    public override Int64 Length { get; } = length >= 0 ? length : throw new ArgumentOutOfRangeException(nameof(length));

    public override Int64 Position
    {
        get => _position;
        set => _ = Seek(value, SeekOrigin.Begin);
    }

    public override ValueTask DisposeAsync()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }

    public override void Flush() { }

    public override Int32 Read(Byte[] buffer, Int32 offset, Int32 count) =>
        ReadAsync(buffer.AsMemory(offset, count), CancellationToken.None).AsTask().GetAwaiter().GetResult();

    public override async ValueTask<Int32> ReadAsync(Memory<Byte> buffer, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (buffer.Length == 0 || _position == Length)
        {
            return 0;
        }

        await EnsureResponseAsync(cancellationToken);
        var remaining = Length - _position;
        var target = buffer[..(Int32)Math.Min(buffer.Length, remaining)];
        Int32 bytesRead;
        try
        {
            bytesRead = await _activeResponse!.Content.ReadAsync(target, cancellationToken);
        }
        catch
        {
            CloseActiveResponse();
            throw;
        }

        if (bytesRead == 0)
        {
            CloseActiveResponse();
            throw new EndOfStreamException("The S3 response ended before the declared object length.");
        }

        _position += bytesRead;
        if (_position == Length)
        {
            CloseActiveResponse();
        }

        return bytesRead;
    }

    public override Int64 Seek(Int64 offset, SeekOrigin origin)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var target = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => checked(_position + offset),
            SeekOrigin.End => checked(Length + offset),
            _ => throw new ArgumentOutOfRangeException(nameof(origin))
        };

        if (target < 0 || target > Length)
        {
            throw new IOException("Cannot seek outside the S3 object's bounds.");
        }

        if (target != _position)
        {
            CloseActiveResponse();
            _position = target;
        }

        return _position;
    }

    public override void SetLength(Int64 value) => throw new NotSupportedException();

    public override void Write(Byte[] buffer, Int32 offset, Int32 count) => throw new NotSupportedException();

    protected override void Dispose(Boolean disposing)
    {
        if (disposing)
        {
            CloseActiveResponse();
            _disposed = true;
        }

        base.Dispose(disposing);
    }

    private void CloseActiveResponse()
    {
        _activeResponse?.Dispose();
        _activeResponse = null;
    }

    private async Task EnsureResponseAsync(CancellationToken cancellationToken)
    {
        if (_activeResponse is null && _position < Length)
        {
            try
            {
                _activeResponse = await client.GetObjectAsync(bucketName, objectKey, _position, cancellationToken);
            }
            catch (S3ObjectNotFoundException exception)
            {
                throw new FileNotFoundException("The requested blob does not exist.", objectKey, exception);
            }
        }
    }
}
