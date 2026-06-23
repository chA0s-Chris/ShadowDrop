// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Uploads;

internal sealed record UploadRawCommandOptions(
    FileInfo[] Files,
    String? ServerUrlOverride,
    String? UploadTokenOverride,
    FileInfo? SecretsOut,
    Boolean Json,
    Boolean Force);
