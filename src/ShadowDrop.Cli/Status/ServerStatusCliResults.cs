// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Status;

using ShadowDrop.Contracts;

internal static class ServerStatusOutcomes
{
    public const String CapabilityDisabled = "capability-disabled";
    public const String Healthy = "healthy";
    public const String NotReady = "not-ready";
    public const String ProtocolIncompatible = "protocol-incompatible";
    public const String Unauthorized = "unauthorized";
    public const String UnexpectedFailure = "unexpected-failure";
    public const String Unreachable = "unreachable";
    public const String UsageError = "usage-error";
}

internal abstract record ServerStatusCliResult(
    String? ServerUrl,
    Boolean Reachable,
    String CliVersion,
    Boolean? ProtocolCompatible,
    String Outcome,
    String? Error);

internal sealed record PublicServerStatusCliResult(
    String? ServerUrl,
    Boolean Reachable,
    String CliVersion,
    Boolean? ProtocolCompatible,
    String Outcome,
    String? Error,
    PublicServerStatusContract? ServerStatus)
    : ServerStatusCliResult(ServerUrl, Reachable, CliVersion, ProtocolCompatible, Outcome, Error);

internal sealed record UploadServerStatusCliResult(
    String? ServerUrl,
    Boolean Reachable,
    String CliVersion,
    Boolean? ProtocolCompatible,
    String Outcome,
    String? Error,
    UploadServerStatusContract? ServerStatus)
    : ServerStatusCliResult(ServerUrl, Reachable, CliVersion, ProtocolCompatible, Outcome, Error);

internal sealed record AdminServerStatusCliResult(
    String? ServerUrl,
    Boolean Reachable,
    String CliVersion,
    Boolean? ProtocolCompatible,
    String Outcome,
    String? Error,
    AdminServerStatusContract? ServerStatus)
    : ServerStatusCliResult(ServerUrl, Reachable, CliVersion, ProtocolCompatible, Outcome, Error);

internal sealed record ServerStatusFailureCliResult(
    String? ServerUrl,
    Boolean Reachable,
    String CliVersion,
    Boolean? ProtocolCompatible,
    String Outcome,
    String? Error,
    ServerStatusMode Mode)
    : ServerStatusCliResult(ServerUrl, Reachable, CliVersion, ProtocolCompatible, Outcome, Error);
