// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Tokens;

/// <summary>
/// The resolved server URL and dedicated admin token used by administrative command surfaces.
/// </summary>
internal readonly record struct ResolvedAdminConfiguration(Uri ServerUrl, String AdminToken);
