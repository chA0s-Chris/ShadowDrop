// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Updates;

using ShadowDrop.Cli.Configuration;
using ShadowDrop.Cli.Terminals;

/// <summary>
/// Runs the unobtrusive post-command update check: rate-limited through the cache, suppressed for
/// non-interactive streams, CI, and the opt-out environment variable, and guaranteed to never change the
/// invoked command's outcome — every failure is swallowed.
/// </summary>
internal static class AutomaticUpdateCheck
{
    /// <summary>Automatic checks contact the release source no more than once per this interval.</summary>
    internal static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);

    internal const String OptOutVariableName = "SHADOWDROP_NO_UPDATE_CHECK";

    public static async Task RunAsync(CliUpdateServices updateServices,
                                      ITerminalCapabilityProvider terminalCapabilityProvider,
                                      TextWriter standardError,
                                      TimeProvider timeProvider,
                                      String installedVersionText,
                                      CancellationToken cancellationToken)
    {
        try
        {
            if (IsSuppressed(updateServices.EnvironmentReader, terminalCapabilityProvider))
            {
                return;
            }

            var latestVersionText = await ResolveLatestVersionAsync(updateServices, timeProvider, cancellationToken);
            if (latestVersionText is null
                || !CliSemanticVersion.TryParse(latestVersionText, out var latest)
                || !CliSemanticVersion.TryParse(installedVersionText, out var installed)
                || latest.CompareTo(installed) <= 0)
            {
                return;
            }

            await standardError.WriteLineAsync(
                $"A newer ShadowDrop release v{latest} is available (installed: v{installedVersionText}). Run 'shadowdrop update' for update instructions.");
        }
        catch
        {
            // The automatic check must never change the success or failure result of the invoked command.
        }
    }

    private static Boolean IsSuppressed(IEnvironmentReader environmentReader, ITerminalCapabilityProvider terminalCapabilityProvider)
    {
        if (EnvironmentValue.IsTruthy(environmentReader.GetEnvironmentVariable(OptOutVariableName))
            || EnvironmentValue.IsTruthy(environmentReader.GetEnvironmentVariable("GITHUB_ACTIONS")))
        {
            return true;
        }

        var standardOutput = terminalCapabilityProvider.DetectForStandardOutput();
        var standardError = terminalCapabilityProvider.DetectForStandardError();
        return standardOutput.IsRedirected || standardError.IsRedirected
                                           || standardOutput.IsCiEnvironment || standardError.IsCiEnvironment;
    }

    private static async Task<String?> ResolveLatestVersionAsync(CliUpdateServices updateServices,
                                                                 TimeProvider timeProvider,
                                                                 CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var cached = updateServices.Cache.Read();
        if (cached is not null && now - cached.CheckedAt < CheckInterval)
        {
            return cached.LatestVersion;
        }

        String? latestVersionText = null;
        try
        {
            latestVersionText = (await updateServices.ReleaseClient.GetLatestStableVersionAsync(cancellationToken)).ToString();
        }
        catch (UpdateCheckException)
        {
            // Failed attempts are recorded below so automatic checks stay within the interval regardless.
        }

        updateServices.Cache.Write(new(now, latestVersionText));
        return latestVersionText;
    }
}
