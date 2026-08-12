// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Results;

using System.Text.Json.Serialization;

internal sealed record UploadDryRunResult(
    [property: JsonPropertyName("status")]
    String Status,
    [property: JsonPropertyName("files")]
    IReadOnlyList<UploadDryRunFile> Files,
    [property: JsonPropertyName("totals")]
    UploadDryRunTotals Totals,
    [property: JsonPropertyName("intendedOutputs")]
    UploadDryRunIntendedOutputs IntendedOutputs,
    [property: JsonPropertyName("uncheckedValidations")]
    IReadOnlyList<String> UncheckedValidations,
    [property: JsonPropertyName("errors")]
    IReadOnlyList<UploadDryRunErrorResult> Errors);

internal sealed record UploadDryRunFile(
    [property: JsonPropertyName("sourcePath")]
    String SourcePath,
    [property: JsonPropertyName("plaintextBytes")]
    Int64 PlaintextBytes,
    [property: JsonPropertyName("encryptedBytes")]
    Int64 EncryptedBytes,
    [property: JsonPropertyName("queueDestination")]
    String? QueueDestination);

internal sealed record UploadDryRunTotals(
    [property: JsonPropertyName("selectedFiles")]
    Int32 SelectedFiles,
    [property: JsonPropertyName("excludedFiles")]
    Int32 ExcludedFiles,
    [property: JsonPropertyName("plaintextBytes")]
    Int64 PlaintextBytes,
    [property: JsonPropertyName("encryptedBytes")]
    Int64 EncryptedBytes);

internal sealed record UploadDryRunIntendedOutputs(
    [property: JsonPropertyName("queueFile")]
    String? QueueFile,
    [property: JsonPropertyName("secretsFile")]
    String? SecretsFile);

internal sealed record UploadDryRunErrorResult(
    [property: JsonPropertyName("message")]
    String Message,
    [property: JsonPropertyName("source")]
    String? Source,
    [property: JsonPropertyName("recordNumber")]
    Int32? RecordNumber);

internal static class UploadDryRunStatus
{
    public const String Invalid = "invalid";
    public const String Valid = "valid";
}
