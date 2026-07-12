// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Configuration;

public sealed class MongoPersistenceOptions
{
    public String ConnectionString { get; set; } = String.Empty;

    public String DatabaseName { get; set; } = String.Empty;
}
