// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Infrastructure;

using System.ComponentModel;

/// <summary>
/// Downloads a URL with the real <c>curl</c> binary. <c>curl</c> is a required prerequisite for the
/// direct-HTTP smoke scenario: if it cannot be launched, this is reported as a hard failure rather than a
/// skip, so a missing tool can never silently hide a regression.
/// </summary>
internal static class CurlClient
{
    public static async Task<ProcessResult> DownloadAsync(String url,
                                                          String outputFilePath,
                                                          String workingDirectory,
                                                          CancellationToken cancellationToken)
    {
        try
        {
            return await ProcessRunner.RunAsync(
                "curl",
                ["--fail", "--silent", "--show-error", "--location", "--output", outputFilePath, url],
                workingDirectory,
                timeout: TimeSpan.FromMinutes(1),
                cancellationToken: cancellationToken);
        }
        catch (Win32Exception exception)
        {
            throw new InvalidOperationException(
                "The 'curl' binary is required for the direct-HTTP end-to-end smoke test but could not be launched. "
                + "Install curl and ensure it is on PATH before running the E2E suite.",
                exception);
        }
    }
}
