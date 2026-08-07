// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Infrastructure.Security;

internal sealed class UploadCredentialProviderUnavailableException : Exception
{
    public UploadCredentialProviderUnavailableException(Exception innerException)
        : base("The upload credential provider is unavailable.", innerException) { }
}
