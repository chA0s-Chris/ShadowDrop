// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Updates;

/// <summary>
/// Produces the official installation command for the current platform. Kept behind an abstraction so a
/// package-manager recommendation can be added later without touching the update command handler.
/// </summary>
internal interface IInstallationGuidanceProvider
{
    String GetInstallCommand();
}
