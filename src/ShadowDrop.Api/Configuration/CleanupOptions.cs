// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Configuration;

public sealed class CleanupOptions
{
    public String CronExpression { get; set; } = "0 */2 * * *";

    /// <summary>
    /// How long a completed upload that no share references is kept before the cleanup run reclaims it.
    /// There is no separate on/off switch: a retention long enough that nothing ever becomes eligible is
    /// how an operator disables reclamation.
    /// </summary>
    public TimeSpan UnreferencedUploadRetention { get; set; } = TimeSpan.FromDays(7);
}
