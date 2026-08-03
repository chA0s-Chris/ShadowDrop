// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Status;

using ShadowDrop.Api.Infrastructure.Security;

internal sealed class ScopedUploadStatusAuthorizationFilter : IEndpointFilter
{
    private readonly TimeSpan _authenticationTimeout;
    private readonly UploadCredentialService _credentialService;

    public ScopedUploadStatusAuthorizationFilter(
        UploadCredentialService credentialService,
        ScopedUploadStatusAuthorizationOptions options)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.AuthenticationTimeout, TimeSpan.Zero);
        _credentialService = credentialService;
        _authenticationTimeout = options.AuthenticationTimeout;
    }

    private static void ObserveLateFault(Task authenticationTask) =>
        _ = authenticationTask.ContinueWith(static task => _ = task.Exception,
                                            CancellationToken.None,
                                            TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnFaulted,
                                            TaskScheduler.Default);

    public async ValueTask<Object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        if (!BearerTokenHeader.TryRead(context.HttpContext.Request.Headers.Authorization, out var token)
            || !UploadCredentialToken.IsInReservedNamespace(token))
        {
            return Results.Unauthorized();
        }

        var requestCancellation = context.HttpContext.RequestAborted;
        using var deadlineCancellation = CancellationTokenSource.CreateLinkedTokenSource(requestCancellation);
        deadlineCancellation.CancelAfter(_authenticationTimeout);
        var authenticationTask = Task.Run(
            () => _credentialService.AuthenticateAsync(token, deadlineCancellation.Token),
            CancellationToken.None);
        ObserveLateFault(authenticationTask);

        UploadCredentialAuthorizationContext? authorizationContext;
        try
        {
            authorizationContext = await authenticationTask.WaitAsync(deadlineCancellation.Token);
            requestCancellation.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException)
        {
            requestCancellation.ThrowIfCancellationRequested();
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
        catch (UploadCredentialProviderUnavailableException)
        {
            requestCancellation.ThrowIfCancellationRequested();
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        if (authorizationContext is null)
        {
            return Results.Unauthorized();
        }

        context.HttpContext.SetUploadAuthorizationContext(authorizationContext);
        return await next(context);
    }
}
