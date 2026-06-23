// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Queues;

internal sealed record QueueCreateCommandOptions(
    String? ShareToken,
    String? ServerUrlOverride,
    FileInfo? Out,
    String? ShareKey,
    FileInfo? ShareKeyFile,
    String? BearerToken,
    Boolean EmbedSecrets,
    Boolean Force);
