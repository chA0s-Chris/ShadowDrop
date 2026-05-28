// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Downloads;

internal sealed class DownloadCommandException : Exception
{
    public DownloadCommandException(String message)
        : base(message) { }

    public DownloadCommandException(String message, Exception innerException)
        : base(message, innerException) { }
}
