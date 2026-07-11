// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Configuration;

public sealed class StorageOptions
{
    public String GridFsBucketName { get; set; } = "shadowdrop_blobs";

    public String LocalRoot { get; set; } = String.Empty;

    public BlobStorageProvider Provider { get; set; } = BlobStorageProvider.FileSystem;
}
