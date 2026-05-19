// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Contracts;

/// <summary>
/// Describes the plaintext byte range requested by a resumable download client.
/// </summary>
public sealed record RequestedPlaintextRangeContract
{
    /// <summary>
    /// Gets or sets the exclusive end offset of the requested plaintext range.
    /// </summary>
    public Int64 End { get; init; }

    /// <summary>
    /// Gets or sets the zero-based start offset of the requested plaintext range.
    /// </summary>
    public Int64 Start { get; init; }
}
