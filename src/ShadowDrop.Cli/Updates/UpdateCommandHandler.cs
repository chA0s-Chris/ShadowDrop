// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Updates;

/// <summary>
/// Handles the explicit <c>update</c> command: performs a live release check (bypassing any cached state),
/// reports whether the installed version is current, and prints the official installation command when a
/// newer stable release exists. It never downloads or executes anything.
/// </summary>
internal sealed class UpdateCommandHandler
{
    private readonly String _installedVersionText;
    private readonly TextWriter _standardError;
    private readonly TextWriter _standardOut;
    private readonly TimeProvider _timeProvider;
    private readonly CliUpdateServices _updateServices;

    public UpdateCommandHandler(CliUpdateServices updateServices,
                                TextWriter standardOut,
                                TextWriter standardError,
                                TimeProvider timeProvider,
                                String installedVersionText)
    {
        _updateServices = updateServices;
        _standardOut = standardOut;
        _standardError = standardError;
        _timeProvider = timeProvider;
        _installedVersionText = installedVersionText;
    }

    public async Task<Int32> ExecuteAsync(CancellationToken cancellationToken)
    {
        CliSemanticVersion latest;
        try
        {
            latest = await _updateServices.ReleaseClient.GetLatestStableVersionAsync(cancellationToken);
        }
        catch (UpdateCheckException exception)
        {
            await _standardError.WriteLineAsync(exception.Message);
            return 1;
        }

        // Refresh the cache so a subsequent automatic check does not repeat the request within the interval.
        _updateServices.Cache.Write(new(_timeProvider.GetUtcNow(), latest.ToString()));

        // An unparseable installed version cannot prove it is current, so it is reported as updatable.
        var upToDate = CliSemanticVersion.TryParse(_installedVersionText, out var installed) && latest.CompareTo(installed) <= 0;

        await _standardOut.WriteLineAsync($"installed-version:{_installedVersionText}");
        await _standardOut.WriteLineAsync($"latest-version:{latest}");
        await _standardOut.WriteLineAsync($"update-status:{(upToDate ? "up-to-date" : "update-available")}");
        if (!upToDate)
        {
            await _standardOut.WriteLineAsync($"update-command:{_updateServices.InstallationGuidance.GetInstallCommand()}");
        }

        return 0;
    }
}
