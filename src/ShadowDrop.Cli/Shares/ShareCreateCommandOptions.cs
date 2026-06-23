// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Shares;

internal sealed record ShareCreateCommandOptions(
    String[] FileIds,
    String? ServerUrlOverride,
    String? UploadTokenOverride,
    String? ExpiresIn,
    Boolean DirectHttp,
    Boolean GenerateDownloadToken,
    FileInfo? SecretsOut,
    Boolean Json,
    Boolean Force);
