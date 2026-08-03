// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Status;

using System.Diagnostics;

internal sealed record OperationalAuditMetadata(String Operation);

internal sealed class AdminStatusAuditEndpointFilter(ILogger<AdminStatusAuditEndpointFilter> logger) : IEndpointFilter
{
    private static String ResolveOutcome(Int32 statusCode) => statusCode switch
    {
        >= 200 and < 400 => "success",
        StatusCodes.Status400BadRequest => "invalid-request",
        StatusCodes.Status401Unauthorized => "unauthorized",
        _ => "failure"
    };

    private void Log(String operation, String outcome, Int32 statusCode, TimeSpan elapsed)
    {
        var elapsedMilliseconds = (Int64)Math.Round(elapsed.TotalMilliseconds, MidpointRounding.AwayFromZero);
        if (outcome is "success" or "cancelled")
        {
            logger.LogInformation(
                "Operational audit: Operation: {Operation}; Outcome: {Outcome}; HttpStatus: {HttpStatus}; ElapsedMilliseconds: {ElapsedMilliseconds}",
                operation, outcome, statusCode, elapsedMilliseconds);
        }
        else if (outcome is "unauthorized" or "invalid-request")
        {
            logger.LogWarning(
                "Operational audit: Operation: {Operation}; Outcome: {Outcome}; HttpStatus: {HttpStatus}; ElapsedMilliseconds: {ElapsedMilliseconds}",
                operation, outcome, statusCode, elapsedMilliseconds);
        }
        else
        {
            logger.LogError(
                "Operational audit: Operation: {Operation}; Outcome: {Outcome}; HttpStatus: {HttpStatus}; ElapsedMilliseconds: {ElapsedMilliseconds}",
                operation, outcome, statusCode, elapsedMilliseconds);
        }
    }

    public async ValueTask<Object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var operation = context.HttpContext.GetEndpoint()?.Metadata.GetMetadata<OperationalAuditMetadata>()?.Operation
                        ?? "unknown";
        var startedAt = Stopwatch.GetTimestamp();
        try
        {
            var result = await next(context);
            var statusCode = result is IStatusCodeHttpResult statusResult
                ? statusResult.StatusCode ?? StatusCodes.Status200OK
                : StatusCodes.Status200OK;
            var outcome = ResolveOutcome(statusCode);
            Log(operation, outcome, statusCode, Stopwatch.GetElapsedTime(startedAt));
            return result;
        }
        catch (OperationCanceledException) when (context.HttpContext.RequestAborted.IsCancellationRequested)
        {
            Log(operation, "cancelled", 499, Stopwatch.GetElapsedTime(startedAt));
            throw;
        }
        catch
        {
            Log(operation, "failure", StatusCodes.Status500InternalServerError, Stopwatch.GetElapsedTime(startedAt));
            throw;
        }
    }
}
