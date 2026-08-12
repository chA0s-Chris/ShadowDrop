// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Uploads;

using System.Collections.Immutable;

/// <summary>
/// A fully materialized, locally validated upload batch. The captured sizes are the source of truth for
/// metadata and encrypted payload lengths during execution.
/// </summary>
internal sealed record LocalUploadPlan(ImmutableArray<LocalUploadFile> Files, Int32 ExcludedFileCount = 0);

/// <summary>
/// One immutable entry in a <see cref="LocalUploadPlan"/>.
/// </summary>
internal sealed record LocalUploadFile(
    FileInfo File,
    UploadSelectionOrigin Origin,
    String? DirectoryRelativePath,
    Int32 FileNumber,
    Int64 PlaintextLength,
    Int64 ChunkCount,
    Int64 EncryptedLength);

/// <summary>
/// Describes where an input selection came from so later command surfaces can produce precise diagnostics.
/// </summary>
internal sealed record UploadSelectionOrigin(String Source, Int32? RecordNumber = null)
{
    /// <summary>
    /// The <see cref="Source"/> value shared by every command-line selection. Diagnostics compare against this
    /// constant instead of repeating the literal.
    /// </summary>
    public const String CommandLineSource = "commandLine";

    public static UploadSelectionOrigin CommandLine { get; } = new(CommandLineSource);
}

/// <summary>
/// A selected source before file-system preflight enriches it with immutable size information.
/// </summary>
internal sealed record UploadSelection(FileInfo File, UploadSelectionOrigin Origin, String? DirectoryRelativePath = null)
{
    public static UploadSelection FromCommandLine(FileInfo file) => new(file, UploadSelectionOrigin.CommandLine);
}

internal sealed record LocalUploadPlanningResult(
    LocalUploadPlan? Plan,
    ImmutableArray<LocalUploadPlanningError> Errors)
{
    public Boolean IsValid => Plan is not null && Errors.IsEmpty;
}

internal sealed record LocalUploadPlanningError(
    FileInfo File,
    UploadSelectionOrigin Origin,
    Int32 FileNumber,
    String Message,
    Int64? EncryptedLength = null);
