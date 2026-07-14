// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Tokens;

using System.Text.Json.Serialization;

internal sealed record UploadCredentialCliListResult(
    [property: JsonPropertyName("credentials")]
    IReadOnlyList<UploadCredentialCliProjection> Credentials,
    [property: JsonPropertyName("nextCursor")]
    String? NextCursor);
