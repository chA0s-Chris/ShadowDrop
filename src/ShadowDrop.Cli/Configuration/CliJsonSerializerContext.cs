// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Configuration;

using ShadowDrop.Cli.Uploads;
using System.Text.Json.Serialization;

[JsonSerializable(typeof(CliConfigFile))]
[JsonSerializable(typeof(UploadReservationResponse))]
[JsonSerializable(typeof(UploadResponse))]
[JsonSerializable(typeof(UploadMetadataPayload))]
internal sealed partial class CliJsonSerializerContext : JsonSerializerContext;
