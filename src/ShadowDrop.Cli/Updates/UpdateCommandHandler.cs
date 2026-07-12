// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Updates;

/// <summary>
/// Handles the explicit <c>update</c> command: performs a live release check (bypassing any cached state),
/// reports whether the installed version is current, and prints the official installation command when a
/// newer stable release exists. It never downloads or executes anything.
/// </summary>
internal sealed class UpdateCommandHandler(
    CliUpdateServices updateServices,
    TextWriter standardOut,
    TextWriter standardError,
    TimeProvider timeProvider,
    String installedVersionText)
{
    public async Task<Int32> ExecuteAsync(CancellationToken cancellationToken)
    {
        CliSemanticVersion latest;
        try
        {
            latest = await updateServices.ReleaseClient.GetLatestStableVersionAsync(cancellationToken);
        }
        catch (UpdateCheckException exception)
        {
            await standardError.WriteLineAsync(exception.Message);
            return 1;
        }

        // Refresh the cache so a subsequent automatic check does not repeat the request within the interval.
        updateServices.Cache.Write(new(timeProvider.GetUtcNow(), latest.ToString()));

        // An unparseable installed version cannot prove it is current, so it is reported as updatable.
        var upToDate = CliSemanticVersion.TryParse(installedVersionText, out var installed) && latest.CompareTo(installed) <= 0;

        await standardOut.WriteLineAsync($"installed-version:{installedVersionText}");
        await standardOut.WriteLineAsync($"latest-version:{latest}");
        await standardOut.WriteLineAsync($"update-status:{(upToDate ? "up-to-date" : "update-available")}");
        if (!upToDate)
        {
            await standardOut.WriteLineAsync($"update-command:{updateServices.InstallationGuidance.GetInstallCommand()}");
        }

        return 0;
    }
}
