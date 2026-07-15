// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Shares;

internal sealed record ShareRevokeCommandOptions(
    String? ShareId,
    String? ServerUrlOverride,
    String? AdminTokenOverride);
