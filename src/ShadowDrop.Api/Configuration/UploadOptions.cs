// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Configuration;

public sealed class UploadOptions
{
    /// <summary>
    /// Gets or sets the maximum accepted upload request body size in bytes. Defaults to 4 GiB.
    /// </summary>
    /// <remarks>
    /// This limits the whole multipart request body (encrypted content plus metadata and multipart overhead), not just the plaintext file size.
    /// </remarks>
    public Int64 MaxBytes { get; set; } = 4L * 1024 * 1024 * 1024;
}
