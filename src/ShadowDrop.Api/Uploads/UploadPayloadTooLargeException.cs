// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Uploads;

public sealed class UploadPayloadTooLargeException : Exception
{
    public UploadPayloadTooLargeException()
        : base("The upload payload exceeds the configured limit.") { }
}
