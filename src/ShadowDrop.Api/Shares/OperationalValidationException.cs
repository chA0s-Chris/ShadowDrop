// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Shares;

public sealed class OperationalValidationException : Exception
{
    public OperationalValidationException(String reason)
        : base("The administrative operational request is invalid.")
    {
        Reason = reason;
    }

    public String Reason { get; }
}
