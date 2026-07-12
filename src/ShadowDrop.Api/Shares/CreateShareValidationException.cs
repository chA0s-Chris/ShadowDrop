// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Shares;

public sealed class CreateShareValidationException : Exception
{
    public CreateShareValidationException(String message) : base(message) { }

    public CreateShareValidationException(String message, Exception innerException) : base(message, innerException) { }
}
