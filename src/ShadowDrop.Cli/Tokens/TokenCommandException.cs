// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Tokens;

internal sealed class TokenCommandException : Exception
{
    public TokenCommandException(String message)
        : base(message) { }

    public TokenCommandException(String message, Exception innerException)
        : base(message, innerException) { }
}
