// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Tokens;

using System.Text.Json.Serialization;

internal sealed record CreateUploadCredentialCliResult(
    [property: JsonPropertyName("credential")]
    UploadCredentialCliProjection Credential,
    [property: JsonPropertyName("token")] String Token);
