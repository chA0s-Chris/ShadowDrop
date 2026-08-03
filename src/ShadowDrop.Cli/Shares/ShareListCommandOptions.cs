// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Shares;

/// <summary>
/// Carries the parsed <c>share list</c> options. <c>Statuses</c> is <see langword="null" /> when <c>--status</c> was
/// omitted entirely and empty when it was supplied without a value; the latter is rejected rather than read as
/// "every share".
/// </summary>
internal sealed record ShareListCommandOptions(
    String[]? Statuses,
    Int32? PageSize,
    String? Cursor,
    String? ServerUrlOverride,
    String? AdminTokenOverride,
    Boolean Json);
