// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Tls;

/// <summary>
/// The resolved TLS trust settings for a single CLI invocation, threaded into the <see cref="System.Net.Http.HttpClient"/> factory.
/// </summary>
/// <param name="CaCertPath">
/// Path to a PEM-encoded certificate to trust as an additional root anchor, or <see langword="null"/> to use the system trust store.
/// </param>
/// <param name="Insecure">When <see langword="true"/>, certificate validation is disabled entirely.</param>
internal sealed record CliTlsOptions(String? CaCertPath, Boolean Insecure)
{
    /// <summary>The default options: validate against the system trust store.</summary>
    public static CliTlsOptions Default { get; } = new(null, false);
}
