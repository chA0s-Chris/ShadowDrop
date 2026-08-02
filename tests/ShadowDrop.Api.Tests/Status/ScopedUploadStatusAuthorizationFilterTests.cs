// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Status;

using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using ShadowDrop.Api.Infrastructure.Security;
using ShadowDrop.Api.Status;

public sealed class ScopedUploadStatusAuthorizationFilterTests
{
    public enum BlockingOperation
    {
        Lookup,
        Usage,
        FailLookup
    }

    [Test]
    public void DefaultAuthenticationTimeout_ShouldBeFiveSeconds()
    {
        ScopedUploadStatusAuthorizationOptions.Default.AuthenticationTimeout.Should().Be(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task InvokeAsync_ShouldPropagateCallerCancellation()
    {
        var (filter, repository, token) = await CreateFilterAsync(BlockingOperation.Lookup, TimeSpan.FromSeconds(5));
        using var cancellation = new CancellationTokenSource();
        var context = CreateContext(token, cancellation.Token);
        var invocation = filter.InvokeAsync(context, UnexpectedNext).AsTask();
        await repository.BlockedOperationStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        try
        {
            await cancellation.CancelAsync();
            var act = async () => await invocation;

            await act.Should().ThrowAsync<OperationCanceledException>();
        }
        finally
        {
            repository.ReleaseBlockedOperation();
            await repository.BlockedOperationCompleted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        }
    }

    [TestCase(BlockingOperation.Lookup)]
    [TestCase(BlockingOperation.Usage)]
    public async Task InvokeAsync_ShouldReturnBodylessServiceUnavailable_WhenAuthenticationOperationStalls(
        BlockingOperation blockingOperation)
    {
        var (filter, repository, token) = await CreateFilterAsync(blockingOperation, TimeSpan.FromMilliseconds(500));
        var context = CreateContext(token);

        try
        {
            var result = await filter.InvokeAsync(context, UnexpectedNext);

            await AssertBodylessServiceUnavailableAsync(result, context.HttpContext);
        }
        finally
        {
            repository.ReleaseBlockedOperation();
            await repository.BlockedOperationCompleted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        }
    }

    [Test]
    public async Task InvokeAsync_ShouldReturnBodylessServiceUnavailable_WhenCredentialProviderFails()
    {
        var (filter, _, token) = await CreateFilterAsync(BlockingOperation.FailLookup, TimeSpan.FromSeconds(5));
        var context = CreateContext(token);

        var result = await filter.InvokeAsync(context, UnexpectedNext);

        await AssertBodylessServiceUnavailableAsync(result, context.HttpContext);
    }

    private static async Task AssertBodylessServiceUnavailableAsync(Object? result, HttpContext httpContext)
    {
        result.Should().BeAssignableTo<IStatusCodeHttpResult>().Which.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
        var responseBody = new MemoryStream();
        httpContext.Response.Body = responseBody;

        await result.Should().BeAssignableTo<IResult>().Subject.ExecuteAsync(httpContext);

        responseBody.Length.Should().Be(0);
    }

    private static DefaultEndpointFilterInvocationContext CreateContext(String token, CancellationToken requestAborted = default)
    {
        var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        var httpContext = new DefaultHttpContext
        {
            RequestAborted = requestAborted,
            RequestServices = services
        };
        httpContext.Request.Headers.Authorization = $"Bearer {token}";
        return new(httpContext, []);
    }

    private static async Task<(ScopedUploadStatusAuthorizationFilter Filter, BlockingUploadCredentialRepository Repository, String Token)>
        CreateFilterAsync(BlockingOperation blockingOperation, TimeSpan timeout)
    {
        var repository = new BlockingUploadCredentialRepository();
        var service = new UploadCredentialService(repository, TimeProvider.System, NullLogger<UploadCredentialService>.Instance);
        var created = await service.CreateAsync(new("status-test", null, null, null), CancellationToken.None);
        repository.Operation = blockingOperation;
        return (new(service, new(timeout)), repository, created.Token);
    }

    private static ValueTask<Object?> UnexpectedNext(EndpointFilterInvocationContext context) =>
        ValueTask.FromException<Object?>(new AssertionException("The status handler must not run."));

    private sealed class BlockingUploadCredentialRepository : IUploadCredentialRepository
    {
        private readonly ManualResetEventSlim _release = new(false);
        private UploadCredentialRecord? _record;

        public TaskCompletionSource BlockedOperationCompleted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource BlockedOperationStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public BlockingOperation Operation { get; set; }

        public void ReleaseBlockedOperation() => _release.Set();

        private void Block(CancellationToken cancellationToken)
        {
            BlockedOperationStarted.TrySetResult();
            try
            {
                _release.Wait();
                cancellationToken.ThrowIfCancellationRequested();
            }
            finally
            {
                BlockedOperationCompleted.TrySetResult();
            }
        }

        public Task<UploadCredentialRecord?> FindBySelectorDigestAsync(
            String selectorDigestBase64,
            CancellationToken cancellationToken)
        {
            if (Operation == BlockingOperation.FailLookup)
            {
                throw new IOException("credential provider failed");
            }

            if (Operation == BlockingOperation.Lookup)
            {
                Block(cancellationToken);
            }

            return Task.FromResult(_record?.SelectorDigestBase64 == selectorDigestBase64 ? _record : null);
        }

        public Task<UploadCredentialRecord?> GetAsync(Guid credentialId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<UploadCredentialPage> ListNewestFirstAsync(
            Int32 pageSize,
            UploadCredentialListCursor? cursor,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task RecordUsageAsync(Guid credentialId, DateTimeOffset lastUsedAtUtc, CancellationToken cancellationToken)
        {
            if (Operation == BlockingOperation.Usage)
            {
                Block(cancellationToken);
            }

            return Task.CompletedTask;
        }

        public Task<UploadCredentialRecord?> RevokeAsync(
            Guid credentialId,
            DateTimeOffset revokedAtUtc,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Boolean> TryCreateAsync(UploadCredentialRecord record, CancellationToken cancellationToken)
        {
            _record = record;
            return Task.FromResult(true);
        }
    }
}
