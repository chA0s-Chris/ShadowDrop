// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Downloads;

using ShadowDrop.Contracts;

/// <summary>
/// Represents a validated streamed CLI download response.
/// </summary>
/// <param name="Metadata">The validated response metadata.</param>
/// <param name="ContentStream">The validated encrypted response body stream.</param>
public sealed record CliDownloadResponse(CliDownloadMetadataContract Metadata, Stream ContentStream);
