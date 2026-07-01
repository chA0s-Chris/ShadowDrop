// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Downloads.Progress;

/// <summary>
/// A synchronous <see cref="IProgress{T}"/> that records the latest reported cumulative byte count and optionally forwards it.
/// </summary>
internal sealed class TrackingProgress(Action<Int64>? onReport = null) : IProgress<Int64>
{
    private Int64 _startingValue = -1;
    private Int64 _value;

    /// <summary>
    /// Gets the byte count transferred since the first reported value, excluding any starting offset from a resumed download.
    /// </summary>
    public Int64 TransferredValue => Value - Math.Max(Interlocked.Read(ref _startingValue), 0);

    /// <summary>
    /// Gets the most recent reported cumulative byte count.
    /// </summary>
    public Int64 Value => Interlocked.Read(ref _value);

    /// <inheritdoc />
    public void Report(Int64 value)
    {
        Interlocked.CompareExchange(ref _startingValue, value, -1);
        Interlocked.Exchange(ref _value, value);
        onReport?.Invoke(value);
    }
}
