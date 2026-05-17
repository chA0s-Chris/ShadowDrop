// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Contracts;

/// <summary>
/// Defines the stable CLI configuration path segments.
/// </summary>
public static class CliConfigPathConstants
{
    /// <summary>
    /// The ShadowDrop application directory name below the user config directory.
    /// </summary>
    public const String ApplicationDirectoryName = "shadowdrop";

    /// <summary>
    /// The user-scoped configuration directory name.
    /// </summary>
    public const String ConfigDirectoryName = ".config";

    /// <summary>
    /// The CLI configuration file name.
    /// </summary>
    public const String FileName = "config.json";
}
