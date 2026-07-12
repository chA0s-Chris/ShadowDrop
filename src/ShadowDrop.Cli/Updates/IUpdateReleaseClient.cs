// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Updates;

/// <summary>
/// Resolves the latest stable ShadowDrop release version from the official release source.
/// </summary>
internal interface IUpdateReleaseClient
{
    /// <summary>
    /// Requests the latest stable, non-draft release version.
    /// </summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The latest stable release version.</returns>
    /// <exception cref="UpdateCheckException">
    /// The release source could not be reached, timed out, or returned malformed
    /// release data.
    /// </exception>
    Task<CliSemanticVersion> GetLatestStableVersionAsync(CancellationToken cancellationToken);
}
