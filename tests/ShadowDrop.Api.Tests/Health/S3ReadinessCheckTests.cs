// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Health;

using FluentAssertions;
using NUnit.Framework;
using ShadowDrop.Api.Health;
using ShadowDrop.Tests.Uploads;

[TestFixture]
public sealed class S3ReadinessCheckTests
{
    [Test]
    public async Task IsReadyAsync_ShouldReturnTrue_WhenBucketCheckSucceeds()
    {
        var check = CreateCheck(new());

        (await check.IsReadyAsync(CancellationToken.None)).Should().BeTrue();
    }

    [Test]
    public async Task IsReadyAsync_ShouldReturnFalse_WhenBucketCheckFails()
    {
        var client = new RecordingS3Client
        {
            CheckBucket = static _ => throw new IOException("unreachable")
        };
        var check = CreateCheck(client);

        (await check.IsReadyAsync(CancellationToken.None)).Should().BeFalse();
    }

    [Test]
    public async Task IsReadyAsync_ShouldReturnFalse_WhenBucketCheckTimesOut()
    {
        var client = new RecordingS3Client
        {
            CheckBucket = static cancellationToken => Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken)
        };
        var check = CreateCheck(client, TimeSpan.FromMilliseconds(20));

        var act = async () => await check.IsReadyAsync(CancellationToken.None);

        (await act.Should().CompleteWithinAsync(TimeSpan.FromSeconds(5))).Which.Should().BeFalse();
    }

    [Test]
    public async Task IsReadyAsync_ShouldPropagateCallerCancellation()
    {
        var client = new RecordingS3Client
        {
            CheckBucket = static cancellationToken => Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken)
        };
        var check = CreateCheck(client);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        // ReSharper disable once AccessToDisposedClosure
        var act = async () => await check.IsReadyAsync(cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private static S3ReadinessCheck CreateCheck(RecordingS3Client client, TimeSpan? timeout = null) =>
        new(client, new()
        {
            Storage = new()
            {
                S3 = new()
                {
                    BucketName = "bucket"
                }
            }
        })
        {
            CheckTimeout = timeout ?? S3ReadinessCheck.DefaultCheckTimeout
        };
}
