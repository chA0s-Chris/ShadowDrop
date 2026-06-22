// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Uploads;

internal sealed record UploadCommandOptions(
    FileInfo[] Files,
    String? ServerUrlOverride,
    String? UploadTokenOverride,
    String? ExpiresIn,
    Boolean DirectHttp,
    Boolean GenerateDownloadToken,
    FileInfo? SecretsOut,
    FileInfo? QueueOut,
    Boolean EmbedSecrets,
    Boolean Json,
    Boolean Force);
