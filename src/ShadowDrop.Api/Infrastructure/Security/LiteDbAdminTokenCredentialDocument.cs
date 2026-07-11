// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Infrastructure.Security;

internal sealed class LiteDbAdminTokenCredentialDocument
{
    public Int32 Id { get; set; }

    public Int32 Iterations { get; set; }

    public String SaltBase64 { get; set; } = String.Empty;

    public String TokenHashBase64 { get; set; } = String.Empty;
}
