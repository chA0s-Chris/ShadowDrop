// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Contracts;

using System.Text.Json.Serialization;

/// <summary>Provides Native AOT-compatible JSON metadata for operational status contracts.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(PublicServerStatusContract))]
[JsonSerializable(typeof(UploadServerStatusContract))]
[JsonSerializable(typeof(AdminServerStatusContract))]
public sealed partial class OperationalStatusJsonSerializerContext : JsonSerializerContext;
