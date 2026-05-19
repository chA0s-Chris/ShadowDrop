// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Downloads;

using FluentAssertions;
using NUnit.Framework;
using ShadowDrop.Cli.Downloads;
using ShadowDrop.Contracts;
using System.Net;

public sealed class CliDownloadResponseParserTests
{
    [Test]
    public void CreateGetRequest_ShouldAppendCliModeAndRangeHeader()
    {
        var request = CliDownloadRequestFactory.CreateGetRequest(new("https://example.test/d/token/files/123"),
                                                                 new()
                                                                 {
                                                                     Start = 64,
                                                                     End = 120
                                                                 });

        request.RequestUri!.Query.Should().Be("?mode=cli");
        request.Headers.Range!.Unit.Should().Be("bytes");
        request.Headers.Range.Ranges.Should().ContainSingle();
        request.Headers.Range.Ranges.Single().From.Should().Be(64);
        request.Headers.Range.Ranges.Single().To.Should().Be(119);
    }

    [Test]
    public async Task Parse_ShouldReturnMetadataAndBodyStream_WhenResponseIsValid()
    {
        using var response = CreateResponse();

        var result = CliDownloadResponseParser.Parse(response,
                                                     new()
                                                     {
                                                         Start = 64,
                                                         End = 120
                                                     });
        await using var stream = result.ContentStream;
        using var content = new MemoryStream();
        await stream.CopyToAsync(content, CancellationToken.None);

        result.Metadata.FirstChunkIndex.Should().Be(1);
        result.Metadata.LastChunkIndex.Should().Be(1);
        result.Metadata.RequestedRange.Should().BeEquivalentTo(new RequestedPlaintextRangeContract
        {
            Start = 64,
            End = 120
        });
        content.ToArray().Should().Equal(Enumerable.Range(0, 80).Select(static value => (Byte)value));
    }

    [Test]
    public async Task Parse_ShouldThrowInvalidDataException_WhenBodyLengthIsShorterThanMetadata()
    {
        using var response = CreateResponse(Enumerable.Range(0, 79).Select(static value => (Byte)value).ToArray(), null);

        var act = () => CliDownloadResponseParser.Parse(response);

        act.Should().Throw<InvalidDataException>().WithMessage("*unexpected body length*");
    }

    [Test]
    public void Parse_ShouldThrowInvalidDataException_WhenDeclaredBodyLengthExceedsMetadata()
    {
        using var response = CreateResponse(Enumerable.Range(0, 81).Select(static value => (Byte)value).ToArray(), null);

        var act = () => CliDownloadResponseParser.Parse(response);

        act.Should().Throw<InvalidDataException>().WithMessage("*unexpected body length*");
    }

    [Test]
    public void Parse_ShouldThrowInvalidDataException_WhenMediaTypeIsUnexpected()
    {
        using var response = CreateResponse();
        response.Content.Headers.ContentType = new("application/json");

        var act = () => CliDownloadResponseParser.Parse(response);

        act.Should().Throw<InvalidDataException>().WithMessage("*unsupported media type*");
    }

    [Test]
    public void Parse_ShouldThrowInvalidDataException_WhenMetadataIsSemanticallyInconsistent()
    {
        using var response = CreateResponse();
        response.Headers.Remove(DownloadHeaderConstants.PlaintextRangeStartHeaderName);
        response.Headers.TryAddWithoutValidation(DownloadHeaderConstants.PlaintextRangeStartHeaderName, "0");

        var act = () => CliDownloadResponseParser.Parse(response);

        act.Should().Throw<InvalidDataException>().WithMessage("*inconsistent plaintext and chunk metadata*");
    }

    [Test]
    public void Parse_ShouldThrowInvalidDataException_WhenRequiredHeaderIsDuplicated()
    {
        using var response = CreateResponse();
        response.Headers.TryAddWithoutValidation(DownloadHeaderConstants.FirstChunkIndexHeaderName, ["1", "2"]);

        var act = () => CliDownloadResponseParser.Parse(response);

        act.Should().Throw<InvalidDataException>().WithMessage($"*{DownloadHeaderConstants.FirstChunkIndexHeaderName}*");
    }

    [Test]
    public void Parse_ShouldThrowInvalidDataException_WhenRequiredHeaderIsMalformed()
    {
        using var response = CreateResponse();
        response.Headers.Remove(DownloadHeaderConstants.TotalPlaintextSizeHeaderName);
        response.Headers.TryAddWithoutValidation(DownloadHeaderConstants.TotalPlaintextSizeHeaderName, "abc");

        var act = () => CliDownloadResponseParser.Parse(response);

        act.Should().Throw<InvalidDataException>().WithMessage($"*{DownloadHeaderConstants.TotalPlaintextSizeHeaderName}*");
    }

    [Test]
    public void Parse_ShouldThrowInvalidDataException_WhenRequiredHeaderIsMissing()
    {
        using var response = CreateResponse();
        response.Headers.Remove(DownloadHeaderConstants.ChunkSizeHeaderName);

        var act = () => CliDownloadResponseParser.Parse(response);

        act.Should().Throw<InvalidDataException>().WithMessage($"*{DownloadHeaderConstants.ChunkSizeHeaderName}*");
    }

    [Test]
    public async Task Parse_ShouldThrowInvalidDataException_WhenTrailingBodyDataAppearsAfterExpectedLength()
    {
        using var response = CreateResponse(Enumerable.Range(0, 81).Select(static value => (Byte)value).ToArray(), 80);

        var parsedResponse = CliDownloadResponseParser.Parse(response);
        await using var stream = parsedResponse.ContentStream;
        using var sink = new MemoryStream();

        var act = async () => await stream.CopyToAsync(sink, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidDataException>().WithMessage("*exceeded the advertised chunk span*");
    }

    private static HttpResponseMessage CreateResponse(Byte[]? body = null, Int64? declaredContentLength = 80)
    {
        body ??= Enumerable.Range(0, 80).Select(static value => (Byte)value).ToArray();
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(body)
        };
        response.Content.Headers.ContentType = new(DownloadHeaderConstants.CliDownloadContentType);
        if (declaredContentLength is not null)
        {
            response.Content.Headers.ContentLength = declaredContentLength;
        }

        response.Headers.TryAddWithoutValidation(DownloadHeaderConstants.FirstChunkIndexHeaderName, "1");
        response.Headers.TryAddWithoutValidation(DownloadHeaderConstants.LastChunkIndexHeaderName, "1");
        response.Headers.TryAddWithoutValidation(DownloadHeaderConstants.PlaintextRangeStartHeaderName, "64");
        response.Headers.TryAddWithoutValidation(DownloadHeaderConstants.PlaintextRangeEndHeaderName, "120");
        response.Headers.TryAddWithoutValidation(DownloadHeaderConstants.TotalPlaintextSizeHeaderName, "128");
        response.Headers.TryAddWithoutValidation(DownloadHeaderConstants.ChunkSizeHeaderName, "64");
        response.Headers.TryAddWithoutValidation(DownloadHeaderConstants.FinalChunkPlaintextLengthHeaderName, "64");
        return response;
    }
}
