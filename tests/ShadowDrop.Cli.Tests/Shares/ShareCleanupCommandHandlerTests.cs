// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Shares;

using FluentAssertions;
using NUnit.Framework;
using ShadowDrop.Cli.Shares;
using ShadowDrop.Tests.Fakes;
using System.Net;

public sealed class ShareCleanupCommandHandlerTests
{
    [Test]
    public async Task ExecuteAsync_ShouldFailWithoutPrintingToken_WhenAuthenticationFails()
    {
        var standardOut = new StringWriter();
        var standardError = new StringWriter();
        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ => new(HttpStatusCode.Unauthorized)));
        var handler = new ShareCleanupCommandHandler(FakeConfiguration.Resolver("https://shadowdrop.test", "secret-upload-token"),
                                                     httpClient,
                                                     standardOut,
                                                     standardError);

        var exitCode = await handler.ExecuteAsync(new(null, null), CancellationToken.None);

        exitCode.Should().Be(1);
        standardOut.ToString().Should().BeEmpty();
        standardError.ToString().Trim().Should().Be("Authentication token invalid or missing.");
        standardError.ToString().Should().NotContain("secret-upload-token");
    }

    [Test]
    public async Task ExecuteAsync_ShouldReportSummaryCounts_WhenCleanupSucceeds()
    {
        var standardOut = new StringWriter();
        var standardError = new StringWriter();
        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ =>
                                                                             new(HttpStatusCode.OK)
                                                                             {
                                                                                 Content = new StringContent(
                                                                                     """{"candidatesScanned":2,"sharesCompleted":1,"blobsDeleted":3,"blobsAlreadyMissing":4,"failures":5,"skipped":false}""")
                                                                             }));
        var handler = new ShareCleanupCommandHandler(FakeConfiguration.Resolver("https://shadowdrop.test", "upload-token"),
                                                     httpClient,
                                                     standardOut,
                                                     standardError);

        var exitCode = await handler.ExecuteAsync(new(null, null), CancellationToken.None);

        exitCode.Should().Be(0);
        standardOut.ToString().Trim().Should()
                   .Be("share-cleanup:candidates-scanned=2 shares-completed=1 blobs-deleted=3 blobs-already-missing=4 failures=5 skipped=false");
        standardError.ToString().Should().BeEmpty();
    }
}
