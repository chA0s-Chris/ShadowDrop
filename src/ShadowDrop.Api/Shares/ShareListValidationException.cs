// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Shares;

public sealed class ShareListValidationException(String reason) : Exception("The administrative share-list request is invalid.")
{
    public String Reason { get; } = reason;
}
