// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Status;

internal sealed record ScopedUploadStatusAuthorizationOptions(TimeSpan AuthenticationTimeout)
{
    public static ScopedUploadStatusAuthorizationOptions Default { get; } = new(TimeSpan.FromSeconds(5));
}
