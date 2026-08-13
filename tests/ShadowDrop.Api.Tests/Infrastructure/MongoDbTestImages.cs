// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Infrastructure;

internal static class MongoDbTestImages
{
    // renovate: datasource=docker depName=mongo versioning=docker
    public const String LatestStable = "mongo:8.3.8";

    // renovate: datasource=docker depName=mongo-5 versioning=docker
    public const String MinimumSupported = "mongo:5.0.33";
}
