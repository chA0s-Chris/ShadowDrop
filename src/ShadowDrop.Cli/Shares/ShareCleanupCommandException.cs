// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Shares;

internal sealed class ShareCleanupCommandException : Exception
{
    public ShareCleanupCommandException(String message) : base(message) { }

    public ShareCleanupCommandException(String message, Exception innerException) : base(message, innerException) { }
}
