// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Downloads;

using ShadowDrop.Contracts;

public sealed record ShareManifestLookupResult(DownloadLookupStatus Status, ShareManifestContract? Manifest = null);
