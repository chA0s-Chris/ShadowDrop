// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Status;

using System.Text.Json.Serialization;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true)]
[JsonSerializable(typeof(PublicServerStatusCliResult))]
[JsonSerializable(typeof(UploadServerStatusCliResult))]
[JsonSerializable(typeof(AdminServerStatusCliResult))]
[JsonSerializable(typeof(ServerStatusFailureCliResult))]
internal sealed partial class ServerStatusCliJsonSerializerContext : JsonSerializerContext;
