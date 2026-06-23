// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Queue;

using System.Text.Json.Serialization;

/// <summary>
/// Source-generated <see cref="JsonSerializerContext"/> for the ShadowDrop queue file format.
/// Required so that <see cref="QueueFileParser"/> works in trimmed/AOT-published applications where
/// reflection-based serialization is disabled.
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(QueueFile))]
internal sealed partial class QueueJsonSerializerContext : JsonSerializerContext;
