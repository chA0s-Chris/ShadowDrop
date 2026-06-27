// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Tls;

/// <summary>
/// Thrown when the TLS trust configuration is invalid, for example when a <c>--cacert</c> file is missing or not a
/// valid PEM-encoded certificate. The message is safe to surface to the user.
/// </summary>
internal sealed class CliTlsConfigurationException : Exception
{
    public CliTlsConfigurationException(String message)
        : base(message) { }

    public CliTlsConfigurationException(String message, Exception innerException)
        : base(message, innerException) { }
}
