// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Status;

using System.Text.Json.Serialization;

internal sealed record ServerStatusCommandOptions(
    String? ServerUrlOverride,
    String? UploadTokenOverride,
    String? AdminTokenOverride,
    Boolean UploadAuthorized,
    Boolean Verbose,
    Boolean Json);

internal enum ServerStatusMode
{
    [JsonStringEnumMemberName("public")]
    Public,

    [JsonStringEnumMemberName("upload")]
    Upload,

    [JsonStringEnumMemberName("admin")]
    Admin
}
