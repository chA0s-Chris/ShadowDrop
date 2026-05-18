// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Contracts;

using System.Text.Json.Serialization;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(CliResumableDownloadContract))]
[JsonSerializable(typeof(FileMetadataContract))]
[JsonSerializable(typeof(RequestedPlaintextRangeContract))]
public partial class ContractsJsonSerializerContext : JsonSerializerContext;
