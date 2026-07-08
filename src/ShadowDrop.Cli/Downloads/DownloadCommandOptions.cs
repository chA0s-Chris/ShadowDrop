// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Downloads;

internal sealed record DownloadCommandOptions(
    String? ShareToken,
    String? ServerUrlOverride,
    String? FileId,
    FileInfo? QueuePath,
    DirectoryInfo? OutputRoot,
    // Bound as a raw String rather than a FileInfo: FileInfo normalizes away the trailing directory separator
    // that DownloadDestinationResolver needs to see to tell a directory destination from a file one.
    String? Out,
    String? ShareKey,
    FileInfo? ShareKeyFile,
    String? BearerToken);
