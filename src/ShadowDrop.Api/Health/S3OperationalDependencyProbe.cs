// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Health;

using ShadowDrop.Api.Configuration;
using ShadowDrop.Api.Uploads;

internal sealed class S3OperationalDependencyProbe : IOperationalDependencyProbe
{
    private readonly IS3Client _client;
    private readonly ShadowDropOptions _options;

    public S3OperationalDependencyProbe(IS3Client client, ShadowDropOptions options)
    {
        _client = client;
        _options = options;
    }

    public IReadOnlyList<String> Components { get; } = ["storage"];

    public String Name => "s3";

    public Task ProbeAsync(CancellationToken cancellationToken) =>
        _client.CheckBucketAsync(_options.Storage.S3.BucketName, cancellationToken);
}
