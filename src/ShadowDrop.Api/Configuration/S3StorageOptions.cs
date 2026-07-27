// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Configuration;

public sealed class S3StorageOptions
{
    public String AccessKeyId { get; set; } = String.Empty;

    public String BucketName { get; set; } = String.Empty;

    public String KeyPrefix { get; set; } = String.Empty;

    public String Region { get; set; } = String.Empty;

    public String SecretAccessKey { get; set; } = String.Empty;

    public String ServiceEndpoint { get; set; } = String.Empty;

    public Boolean UsePathStyle { get; set; }
}
