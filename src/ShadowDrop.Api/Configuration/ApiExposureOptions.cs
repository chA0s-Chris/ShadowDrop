// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Configuration;

public sealed class ApiExposureOptions
{
    public Boolean EnableAdminOperations { get; set; } = true;

    public Boolean EnablePublicDownloads { get; set; } = true;

    public Boolean? EnableUploads { get; set; }

    public Boolean UploadsEnabled => EnableUploads ?? EnableAdminOperations;
}
