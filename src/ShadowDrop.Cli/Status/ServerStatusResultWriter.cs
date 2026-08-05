// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Status;

using ShadowDrop.Contracts;
using System.Text.Json;

internal static class ServerStatusResultWriter
{
    public static async Task WriteAsync(ServerStatusCliResult result, Boolean json, TextWriter standardOut)
    {
        if (json)
        {
            var output = result switch
            {
                PublicServerStatusCliResult publicResult =>
                    JsonSerializer.Serialize(publicResult, ServerStatusCliJsonSerializerContext.Default.PublicServerStatusCliResult),
                UploadServerStatusCliResult uploadResult =>
                    JsonSerializer.Serialize(uploadResult, ServerStatusCliJsonSerializerContext.Default.UploadServerStatusCliResult),
                AdminServerStatusCliResult adminResult =>
                    JsonSerializer.Serialize(adminResult, ServerStatusCliJsonSerializerContext.Default.AdminServerStatusCliResult),
                ServerStatusFailureCliResult failureResult =>
                    JsonSerializer.Serialize(failureResult, ServerStatusCliJsonSerializerContext.Default.ServerStatusFailureCliResult),
                _ => throw new ArgumentOutOfRangeException(nameof(result), result, "Unsupported status result type.")
            };
            await standardOut.WriteLineAsync(output);
            return;
        }

        await standardOut.WriteLineAsync($"server-url:{result.ServerUrl ?? "unavailable"}");
        await standardOut.WriteLineAsync($"reachability:{(result.Reachable ? "reachable" : "unreachable")}");
        await standardOut.WriteLineAsync($"cli-version:{result.CliVersion}");
        await standardOut.WriteLineAsync($"protocol-compatible:{Format(result.ProtocolCompatible)}");
        await standardOut.WriteLineAsync($"outcome:{result.Outcome}");

        switch (result)
        {
            case PublicServerStatusCliResult { ServerStatus: { } status }:
                await WriteStatusAsync(status.ProtocolVersion, status.Live, status.Ready, status.Reason, status.Capabilities, standardOut);
                break;
            case UploadServerStatusCliResult { ServerStatus: { } status }:
                await WriteStatusAsync(status.ProtocolVersion, status.Live, status.Ready, status.Reason, status.Capabilities, standardOut);
                await standardOut.WriteLineAsync($"max-file-bytes:{Format(status.EffectiveLimits.MaxFileBytes)}");
                await standardOut.WriteLineAsync($"max-share-bytes:{Format(status.EffectiveLimits.MaxShareBytes)}");
                await standardOut.WriteLineAsync($"expires-at-utc:{Format(status.EffectiveLimits.ExpiresAtUtc)}");
                break;
            case AdminServerStatusCliResult { ServerStatus: { } status }:
                await WriteStatusAsync(status.ProtocolVersion, status.Live, status.Ready, status.Reason, status.Capabilities, standardOut);
                await standardOut.WriteLineAsync($"build-version:{status.BuildVersion}");
                await standardOut.WriteLineAsync($"uptime-seconds:{status.UptimeSeconds}");
                await standardOut.WriteLineAsync($"metadata-provider:{status.Providers.Metadata}");
                await standardOut.WriteLineAsync($"storage-provider:{status.Providers.Storage}");
                if (status.Components.Length == 0)
                {
                    await standardOut.WriteLineAsync("components:none");
                }
                else
                {
                    foreach (var component in status.Components.OrderBy(static component => component.Name, StringComparer.Ordinal))
                    {
                        await standardOut.WriteLineAsync($"component:{component.Name}:{component.State}:{component.Reason}");
                    }
                }

                await standardOut.WriteLineAsync($"storage-files:{Format(status.Storage.CompletedFileCount)}");
                await standardOut.WriteLineAsync($"storage-ciphertext-bytes:{Format(status.Storage.CiphertextBytes)}");
                await standardOut.WriteLineAsync($"shares-active:{Format(status.Shares?.Active)}");
                await standardOut.WriteLineAsync($"shares-expired:{Format(status.Shares?.Expired)}");
                await standardOut.WriteLineAsync($"shares-revoked:{Format(status.Shares?.Revoked)}");
                await standardOut.WriteLineAsync($"shares-cleanup-pending:{Format(status.Shares?.CleanupPending)}");
                await standardOut.WriteLineAsync($"shares-cleanup-failed:{Format(status.Shares?.CleanupFailed)}");
                await standardOut.WriteLineAsync($"cleanup-last-run-at-utc:{Format(status.Cleanup.LastRunAtUtc)}");
                await standardOut.WriteLineAsync($"cleanup-outcome:{status.Cleanup.LastOutcome}");
                await standardOut.WriteLineAsync($"resumable-sessions-active:{Format(status.ResumableSessions.ActiveCount)}");
                if (status.ConfigurationWarnings.Length == 0)
                {
                    await standardOut.WriteLineAsync("configuration-warnings:none");
                }
                else
                {
                    foreach (var warning in status.ConfigurationWarnings.Order(StringComparer.Ordinal))
                    {
                        await standardOut.WriteLineAsync($"configuration-warning:{warning}");
                    }
                }

                break;
        }
    }

    private static String Format(Boolean? value) => value?.ToString().ToLowerInvariant() ?? "unavailable";

    private static String Format(DateTimeOffset? value) => value?.ToString("O") ?? "unavailable";

    private static String Format(Int64? value) => value?.ToString() ?? "unavailable";

    private static async Task WriteStatusAsync(
        Int32 protocolVersion,
        Boolean live,
        Boolean ready,
        String reason,
        StatusCapabilitiesContract capabilities,
        TextWriter standardOut)
    {
        await standardOut.WriteLineAsync($"protocol-version:{protocolVersion}");
        await standardOut.WriteLineAsync($"live:{live.ToString().ToLowerInvariant()}");
        await standardOut.WriteLineAsync($"ready:{ready.ToString().ToLowerInvariant()}");
        await standardOut.WriteLineAsync($"reason:{reason}");
        await standardOut.WriteLineAsync($"capability-public-downloads:{capabilities.PublicDownloads.ToString().ToLowerInvariant()}");
        await standardOut.WriteLineAsync($"capability-admin-operations:{capabilities.AdminOperations.ToString().ToLowerInvariant()}");
        await standardOut.WriteLineAsync($"capability-resumable-downloads:{capabilities.ResumableDownloads.ToString().ToLowerInvariant()}");
        await standardOut.WriteLineAsync($"capability-scoped-uploads:{capabilities.ScopedUploads.ToString().ToLowerInvariant()}");
    }
}
