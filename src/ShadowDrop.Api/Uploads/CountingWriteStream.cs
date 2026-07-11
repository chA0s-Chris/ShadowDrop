// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Uploads;

internal sealed class CountingWriteStream(Stream inner) : Stream
{
    public Int64 BytesWritten { get; private set; }
    public override Boolean CanRead => false;
    public override Boolean CanSeek => false;
    public override Boolean CanWrite => true;
    public override Int64 Length => BytesWritten;
    public override Int64 Position { get => BytesWritten; set => throw new NotSupportedException(); }
    public override ValueTask DisposeAsync() => ValueTask.CompletedTask;
    public override void Flush() => inner.Flush();
    public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);
    public override Int32 Read(Byte[] buffer, Int32 offset, Int32 count) => throw new NotSupportedException();
    public override Int64 Seek(Int64 offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(Int64 value) => throw new NotSupportedException();

    public override void Write(Byte[] buffer, Int32 offset, Int32 count)
    {
        inner.Write(buffer, offset, count);
        BytesWritten += count;
    }

    public override async ValueTask WriteAsync(ReadOnlyMemory<Byte> buffer, CancellationToken cancellationToken = default)
    {
        await inner.WriteAsync(buffer, cancellationToken);
        BytesWritten += buffer.Length;
    }

    protected override void Dispose(Boolean disposing) { }
}
