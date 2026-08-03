// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Status;

using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using NUnit.Framework;
using ShadowDrop.Api.Status;

public sealed class OperationalAuditEndpointFilterTests
{
    [Test]
    public async Task InvokeAsync_ShouldAuditCallerCancellation_AndRethrow()
    {
        var collector = new FakeLogCollector();
        var filter = new OperationalAuditEndpointFilter(new FakeLogger<OperationalAuditEndpointFilter>(collector));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var context = CreateContext(cancellation.Token);

        var act = async () => await filter.InvokeAsync(
            context,
            static _ => ValueTask.FromException<Object?>(new OperationCanceledException()));

        await act.Should().ThrowAsync<OperationCanceledException>();
        AssertAuditRecord(collector, LogLevel.Information, "cancelled", 499);
    }

    [TestCase(StatusCodes.Status200OK, "success", LogLevel.Information)]
    [TestCase(StatusCodes.Status400BadRequest, "invalid-request", LogLevel.Warning)]
    [TestCase(StatusCodes.Status503ServiceUnavailable, "failure", LogLevel.Error)]
    public async Task InvokeAsync_ShouldAuditCompletedOutcome(
        Int32 statusCode,
        String outcome,
        LogLevel level)
    {
        var collector = new FakeLogCollector();
        var filter = new OperationalAuditEndpointFilter(new FakeLogger<OperationalAuditEndpointFilter>(collector));
        var context = CreateContext();

        _ = await filter.InvokeAsync(context,
                                     _ => ValueTask.FromResult<Object?>(Results.StatusCode(statusCode)));

        AssertAuditRecord(collector, level, outcome, statusCode);
    }

    [Test]
    public async Task InvokeAsync_ShouldAuditUnauthorizedResponse_WithAllowListedPropertiesOnly()
    {
        var collector = new FakeLogCollector();
        var filter = new OperationalAuditEndpointFilter(new FakeLogger<OperationalAuditEndpointFilter>(collector));
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Authorization = "Bearer secret-token";
        httpContext.SetEndpoint(new(static _ => Task.CompletedTask,
                                    new(new OperationalAuditMetadata("admin-status-view")),
                                    "status"));
        var context = new DefaultEndpointFilterInvocationContext(httpContext, []);

        _ = await filter.InvokeAsync(context, static _ => ValueTask.FromResult<Object?>(Results.Unauthorized()));

        var records = collector.GetSnapshot();
        records.Should().ContainSingle();
        var record = records[0];
        record.Level.Should().Be(LogLevel.Warning);
        record.Message.Should().Contain("admin-status-view").And.Contain("unauthorized").And.NotContain("secret-token");
        record.Exception.Should().BeNull();
        record.StructuredState!.Where(pair => pair.Key != "{OriginalFormat}").Select(pair => pair.Key).Should().BeEquivalentTo(
            "Operation", "Outcome", "HttpStatus", "ElapsedMilliseconds");
    }

    [Test]
    public async Task InvokeAsync_ShouldAuditUnhandledFailureWithoutLoggingException_AndRethrow()
    {
        var collector = new FakeLogCollector();
        var filter = new OperationalAuditEndpointFilter(new FakeLogger<OperationalAuditEndpointFilter>(collector));
        var context = CreateContext();

        var act = async () => await filter.InvokeAsync(
            context,
            static _ => ValueTask.FromException<Object?>(new InvalidOperationException("sensitive detail")));

        await act.Should().ThrowAsync<InvalidOperationException>();
        var record = AssertAuditRecord(collector, LogLevel.Error, "failure", StatusCodes.Status500InternalServerError);
        record.Message.Should().NotContain("sensitive detail");
    }

    private static FakeLogRecord AssertAuditRecord(
        FakeLogCollector collector,
        LogLevel level,
        String outcome,
        Int32 statusCode)
    {
        var records = collector.GetSnapshot();
        records.Should().ContainSingle();
        var record = records[0];
        record.Level.Should().Be(level);
        record.Message.Should().Contain("admin-status-view").And.Contain(outcome).And.Contain(statusCode.ToString());
        record.Exception.Should().BeNull();
        record.StructuredState!.Where(pair => pair.Key != "{OriginalFormat}").Select(pair => pair.Key).Should().BeEquivalentTo(
            "Operation", "Outcome", "HttpStatus", "ElapsedMilliseconds");
        return record;
    }

    private static DefaultEndpointFilterInvocationContext CreateContext(CancellationToken requestAborted = default)
    {
        var httpContext = new DefaultHttpContext
        {
            RequestAborted = requestAborted
        };
        httpContext.SetEndpoint(new(static _ => Task.CompletedTask,
                                    new(new OperationalAuditMetadata("admin-status-view")),
                                    "status"));
        return new(httpContext, []);
    }
}
