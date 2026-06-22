// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Fakes;

/// <summary>
/// A configurable <see cref="HttpMessageHandler"/> that returns a caller-supplied response or throws a caller-supplied
/// exception, for exercising HTTP client error handling without a live server.
/// </summary>
internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

    public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

    public StubHttpMessageHandler(HttpResponseMessage response)
        : this(_ => response) { }

    public static StubHttpMessageHandler Throwing(Exception exception) => new(_ => throw exception);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(_responder(request));
}

/// <summary>
/// Returns a fixed sequence of responses, one per request, in order. Throws if more requests arrive than configured.
/// </summary>
internal sealed class SequenceHttpMessageHandler(params Func<HttpRequestMessage, HttpResponseMessage>[] responses) : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses = new(responses);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (_responses.Count == 0)
        {
            throw new InvalidOperationException("Unexpected extra HTTP request.");
        }

        return Task.FromResult(_responses.Dequeue()(request));
    }
}
