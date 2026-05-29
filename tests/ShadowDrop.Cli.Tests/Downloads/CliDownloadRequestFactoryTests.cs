// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Downloads;

using FluentAssertions;
using NUnit.Framework;
using ShadowDrop.Cli.Downloads;

public sealed class CliDownloadRequestFactoryTests
{
    [Test]
    public void CreateGetRequest_ShouldAppendCliMode_WhenNoRangeIsRequested()
    {
        var request = CliDownloadRequestFactory.CreateGetRequest(new("https://shadowdrop.test/d/token/files/01234567-89ab-cdef-0123-456789abcdef"));

        request.Method.Should().Be(HttpMethod.Get);
        request.RequestUri.Should().NotBeNull();
        request.RequestUri!.Query.Should().Be("?mode=cli");
        request.Headers.Range.Should().BeNull();
    }

    [Test]
    public void CreateGetRequest_ShouldAttachAuthorizationHeader_WhenBearerTokenIsProvided()
    {
        var request = CliDownloadRequestFactory.CreateGetRequest(new("https://shadowdrop.test/d/token/files/01234567-89ab-cdef-0123-456789abcdef"),
                                                                 "download-token");

        request.Headers.Authorization.Should().NotBeNull();
        request.Headers.Authorization!.Scheme.Should().Be("Bearer");
        request.Headers.Authorization.Parameter.Should().Be("download-token");
    }

    [TestCase("https://shadowdrop.test/d/token/files/01234567-89ab-cdef-0123-456789abcdef?mode=cli", "?mode=cli")]
    [TestCase("https://shadowdrop.test/d/token/files/01234567-89ab-cdef-0123-456789abcdef?mode=direct-http", "?mode=cli")]
    [TestCase("https://shadowdrop.test/d/token/files/01234567-89ab-cdef-0123-456789abcdef?download=1&mode=direct-http&mode=cli", "?download=1&mode=cli")]
    public void CreateGetRequest_ShouldNormalizeModeQueryParameter_ToSingleCliValue(String downloadUri, String expectedQuery)
    {
        var request = CliDownloadRequestFactory.CreateGetRequest(new(downloadUri));

        request.RequestUri.Should().NotBeNull();
        request.RequestUri!.Query.Should().Be(expectedQuery);
        request.Headers.Range.Should().BeNull();
    }

    [Test]
    public void CreateGetRequest_ShouldPreserveExistingQueryString_AndAddByteRangeHeader()
    {
        var request = CliDownloadRequestFactory.CreateGetRequest(new("https://shadowdrop.test/d/token/files/01234567-89ab-cdef-0123-456789abcdef?download=1"),
                                                                 requestedRange: new()
                                                                 {
                                                                     Start = 64,
                                                                     End = 120
                                                                 });

        request.RequestUri.Should().NotBeNull();
        request.RequestUri!.Query.Should().Be("?download=1&mode=cli");
        request.Headers.Range.Should().NotBeNull();
        request.Headers.Range!.Unit.Should().Be("bytes");
        request.Headers.Range.Ranges.Should().ContainSingle();
        request.Headers.Range.Ranges.Single().From.Should().Be(64);
        request.Headers.Range.Ranges.Single().To.Should().Be(119);
    }

    [TestCase(-1, 10)]
    [TestCase(10, 10)]
    [TestCase(10, 9)]
    public void CreateGetRequest_ShouldRejectInvalidRequestedRanges(Int64 start, Int64 end)
    {
        var act = () => CliDownloadRequestFactory.CreateGetRequest(new("https://shadowdrop.test/d/token/files/01234567-89ab-cdef-0123-456789abcdef"),
                                                                   requestedRange: new()
                                                                   {
                                                                       Start = start,
                                                                       End = end
                                                                   });

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
