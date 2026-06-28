// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Configuration;

public sealed class ShadowDropOptions
{
    public ApiExposureOptions ApiExposure { get; set; } = new();

    public CleanupOptions Cleanup { get; set; } = new();

    public MetadataOptions Metadata { get; set; } = new();

    public StorageOptions Storage { get; set; } = new();
}
