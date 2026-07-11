// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Infrastructure;

internal static class MongoDbTestImages
{
    // renovate: datasource=docker depName=mongo packageName=mongo
    public const String LatestStable = "mongo:8.3.4";

    // renovate: datasource=docker depName=mongo-5 packageName=mongo
    public const String MinimumSupported = "mongo:5.0.31";
}
