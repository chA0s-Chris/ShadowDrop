// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Updates;

using System.Text.Json.Serialization;

/// <summary>
/// The subset of the GitHub release payload the update check needs; everything else is ignored so the
/// source-generated deserializer stays small and Native-AOT compatible.
/// </summary>
internal sealed record GitHubReleaseContract(
    [property: JsonPropertyName("tag_name")]
    String? TagName,
    [property: JsonPropertyName("draft")]
    Boolean Draft,
    [property: JsonPropertyName("prerelease")]
    Boolean Prerelease);
