// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
#if ENABLE_THROTTLE_DOWNLOAD
namespace ShadowDrop.Api.Downloads;

using System.Diagnostics;

/// <summary>
/// DEVELOPMENT-ONLY write decorator that paces writes to an inner stream to a target byte rate.
/// </summary>
/// <remarks>
/// Streamed downloads complete near-instantly over loopback, which makes the CLI's live progress output impossible to observe.
/// Wrapping the response body in this stream slows the transfer to a steady rate so the spinner, percentage, speed, and ETA render.
/// The type is compiled only when the <c>ENABLE_THROTTLE_DOWNLOAD</c> symbol is defined (Debug builds), so it is physically absent
/// from Release binaries.
/// <para>
/// Ownership: this decorator deliberately does not own or dispose <paramref name="inner"/>. The throttle middleware swaps it onto
/// <c>HttpResponse.Body</c> and restores the original body in a <c>finally</c> without ever disposing the wrapper, and the inner
/// response body is owned by Kestrel. Disposal is therefore intentionally not forwarded.
/// </para>
/// </remarks>
internal sealed class ThrottledStream(Stream inner, Int64 bytesPerSecond) : Stream
{
    // Validate here so the decorator is self-contained: a non-positive rate would divide by zero or produce nonsensical
    // pacing, regardless of whether the constructing middleware guards the value.
    private readonly Int64 _bytesPerSecond = bytesPerSecond > 0
        ? bytesPerSecond
        : throw new ArgumentOutOfRangeException(nameof(bytesPerSecond), bytesPerSecond, "Throttle rate must be a positive number of bytes per second.");

    private readonly Int64 _startTimestamp = Stopwatch.GetTimestamp();
    private Int64 _bytesWritten;

    public override Boolean CanRead => false;

    public override Boolean CanSeek => false;

    public override Boolean CanWrite => true;

    public override Int64 Length => throw new NotSupportedException();

    public override Int64 Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush() => inner.Flush();

    public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);

    public override Int32 Read(Byte[] buffer, Int32 offset, Int32 count) => throw new NotSupportedException();

    public override Int64 Seek(Int64 offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(Int64 value) => throw new NotSupportedException();

    public override void Write(Byte[] buffer, Int32 offset, Int32 count)
    {
        inner.Write(buffer, offset, count);
        // Sync-over-async pacing: this path is effectively dead because Kestrel disallows synchronous body writes by default
        // (AllowSynchronousIO is false), so the response body is always written via the async overloads above. Kept only to
        // satisfy the abstract Stream contract for this development-only decorator.
        DelayToMatchRateAsync(count, CancellationToken.None).AsTask().GetAwaiter().GetResult();
    }

    public override Task WriteAsync(Byte[] buffer, Int32 offset, Int32 count, CancellationToken cancellationToken) =>
        WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override async ValueTask WriteAsync(ReadOnlyMemory<Byte> buffer, CancellationToken cancellationToken = default)
    {
        await inner.WriteAsync(buffer, cancellationToken);
        await DelayToMatchRateAsync(buffer.Length, cancellationToken);
    }

    private async ValueTask DelayToMatchRateAsync(Int32 byteCount, CancellationToken cancellationToken)
    {
        _bytesWritten += byteCount;

        // Sleep off the difference between how long the bytes so far should have taken at the target rate and how long they actually took.
        var targetElapsed = TimeSpan.FromSeconds((Double)_bytesWritten / _bytesPerSecond);
        var actualElapsed = Stopwatch.GetElapsedTime(_startTimestamp);
        var delay = targetElapsed - actualElapsed;
        if (delay > TimeSpan.Zero)
        {
            await Task.Delay(delay, cancellationToken);
        }
    }
}
#endif
