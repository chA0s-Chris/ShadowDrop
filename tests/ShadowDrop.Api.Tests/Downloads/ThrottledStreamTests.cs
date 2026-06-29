// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
#if ENABLE_THROTTLE_DOWNLOAD
namespace ShadowDrop.Tests.Downloads;

using FluentAssertions;
using NUnit.Framework;
using ShadowDrop.Api.Downloads;

public sealed class ThrottledStreamTests
{
    [Test]
    public void Capabilities_ShouldBeWriteOnly()
    {
        var throttled = new ThrottledStream(new MemoryStream(), 1000);

        throttled.CanWrite.Should().BeTrue();
        throttled.CanRead.Should().BeFalse();
        throttled.CanSeek.Should().BeFalse();
    }

    [Test]
    public async Task WriteAsync_ShouldForwardAllBytesToInnerStream()
    {
        var inner = new MemoryStream();
        // A very high rate keeps the pacing delay negligible so the test does not depend on wall-clock timing.
        var throttled = new ThrottledStream(inner, Int64.MaxValue);
        var payload = Enumerable.Range(0, 4096).Select(static value => (Byte)value).ToArray();

        await throttled.WriteAsync(payload);
        await throttled.FlushAsync(CancellationToken.None);

        inner.ToArray().Should().Equal(payload);
    }
}
#endif
