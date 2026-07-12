// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Updates;

/// <summary>
/// The persisted result of an update check. <see cref="LatestVersion"/> is <see langword="null"/> when the
/// check failed; the attempt is still recorded so automatic checks contact the release source no more than
/// once per interval even across failures.
/// </summary>
internal sealed record UpdateCheckRecord(DateTimeOffset CheckedAt, String? LatestVersion);
