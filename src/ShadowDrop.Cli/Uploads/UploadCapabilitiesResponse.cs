// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Uploads;

using System.Text.Json.Serialization;

internal sealed record UploadCapabilitiesResponse(
    [property: JsonPropertyName("maxFilePayloadBytes")]
    Int64 MaxFilePayloadBytes);
