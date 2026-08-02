// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Contracts;

/// <summary>Defines the operational status protocol versions supported by this release.</summary>
public static class OperationalStatusProtocol
{
    public const Int32 CurrentVersion = 1;

    public const Int32 MaximumSupportedVersion = 1;

    public const Int32 MinimumSupportedVersion = 1;
}

/// <summary>Stable readiness reasons exposed by the operational status API.</summary>
public static class OperationalStatusReasons
{
    public const String CapabilityDisabled = "capability-disabled";

    public const String DependencyTimeout = "dependency-timeout";

    public const String DependencyUnavailable = "dependency-unavailable";

    public const String None = "none";
}

/// <summary>Stable component states exposed by administrative status.</summary>
public static class OperationalComponentStates
{
    public const String NotApplicable = "not-applicable";

    public const String NotReady = "not-ready";

    public const String Ready = "ready";
}

/// <summary>Stable configuration warning codes exposed by administrative status.</summary>
public static class OperationalStatusWarnings
{
    public const String StorageAccountingIncomplete = "storage-accounting-incomplete";
}

/// <summary>Describes the server capabilities that are currently exposed.</summary>
public sealed record StatusCapabilitiesContract(
    Boolean PublicDownloads,
    Boolean AdminOperations,
    Boolean ResumableDownloads,
    Boolean ScopedUploads);

/// <summary>Contains the anonymous operational status projection.</summary>
public sealed record PublicServerStatusContract(
    Int32 ProtocolVersion,
    Boolean Live,
    Boolean Ready,
    String Reason,
    StatusCapabilitiesContract Capabilities);

/// <summary>Contains effective limits for an authenticated scoped upload credential.</summary>
public sealed record StatusEffectiveLimitsContract(
    Int64? MaxFileBytes,
    Int64? MaxShareBytes,
    DateTimeOffset? ExpiresAtUtc);

/// <summary>Contains the scoped-upload-authorized operational status projection.</summary>
public sealed record UploadServerStatusContract(
    Int32 ProtocolVersion,
    Boolean Live,
    Boolean Ready,
    String Reason,
    StatusCapabilitiesContract Capabilities,
    StatusEffectiveLimitsContract EffectiveLimits);

/// <summary>Describes one allow-listed logical dependency component.</summary>
public sealed record StatusComponentContract(String Name, String State, String Reason);

/// <summary>Names the configured persistence providers without exposing their configuration.</summary>
public sealed record StatusProvidersContract(String Metadata, String Storage);

/// <summary>Contains exact retained-blob totals when persisted accounting is complete.</summary>
public sealed record StatusStorageContract(Int64? CompletedFileCount, Int64? CiphertextBytes);

/// <summary>Contains lifecycle and cleanup share counts.</summary>
public sealed record StatusSharesContract(
    Int64 Active,
    Int64 Expired,
    Int64 Revoked,
    Int64 CleanupPending,
    Int64 CleanupFailed,
    Int64 CleanupCompleted);

/// <summary>Contains process-local cleanup-run state.</summary>
public sealed record StatusCleanupContract(DateTimeOffset? LastRunAtUtc, String LastOutcome);

/// <summary>Contains server-side resumable-session state when such sessions exist.</summary>
public sealed record StatusResumableSessionsContract(Int64? ActiveCount);

/// <summary>Contains the administrator-authorized operational status projection.</summary>
public sealed record AdminServerStatusContract(
    Int32 ProtocolVersion,
    Boolean Live,
    Boolean Ready,
    String Reason,
    StatusCapabilitiesContract Capabilities,
    String BuildVersion,
    Int64 UptimeSeconds,
    StatusComponentContract[] Components,
    StatusProvidersContract Providers,
    StatusStorageContract Storage,
    StatusSharesContract? Shares,
    StatusCleanupContract Cleanup,
    StatusResumableSessionsContract ResumableSessions,
    String[] ConfigurationWarnings);
