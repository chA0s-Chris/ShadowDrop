// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api.Status;

using System.Diagnostics;

internal sealed record OperationalAuditMetadata(String Operation);

internal sealed class OperationalAuditEndpointFilter : IEndpointFilter
{
    internal const String FilenamesIncludedItemKey = "ShadowDrop.AdminShareInspect.FilenamesIncluded";
    internal const String ShareInspectionOperation = "admin-share-inspect";

    private readonly ILogger<OperationalAuditEndpointFilter> _logger;

    public OperationalAuditEndpointFilter(ILogger<OperationalAuditEndpointFilter> logger)
    {
        _logger = logger;
    }

    private static String ResolveOutcome(Int32 statusCode) => statusCode switch
    {
        >= 200 and < 400 => "success",
        StatusCodes.Status400BadRequest => "invalid-request",
        StatusCodes.Status401Unauthorized => "unauthorized",
        _ => "failure"
    };

    private void Log(String operation, String outcome, Int32 statusCode, TimeSpan elapsed, HttpContext context)
    {
        var elapsedMilliseconds = (Int64)Math.Round(elapsed.TotalMilliseconds, MidpointRounding.AwayFromZero);
        if (String.Equals(operation, ShareInspectionOperation, StringComparison.Ordinal))
        {
            LogShareInspection(operation, outcome, statusCode, elapsedMilliseconds, context);
            return;
        }

        if (outcome is "success" or "cancelled")
        {
            _logger.LogInformation(
                "Operational audit: Operation: {Operation}; Outcome: {Outcome}; HttpStatus: {HttpStatus}; ElapsedMilliseconds: {ElapsedMilliseconds}",
                operation, outcome, statusCode, elapsedMilliseconds);
        }
        else if (outcome is "unauthorized" or "invalid-request")
        {
            _logger.LogWarning(
                "Operational audit: Operation: {Operation}; Outcome: {Outcome}; HttpStatus: {HttpStatus}; ElapsedMilliseconds: {ElapsedMilliseconds}",
                operation, outcome, statusCode, elapsedMilliseconds);
        }
        else
        {
            _logger.LogError(
                "Operational audit: Operation: {Operation}; Outcome: {Outcome}; HttpStatus: {HttpStatus}; ElapsedMilliseconds: {ElapsedMilliseconds}",
                operation, outcome, statusCode, elapsedMilliseconds);
        }
    }

    private void LogShareInspection(
        String operation,
        String outcome,
        Int32 statusCode,
        Int64 elapsedMilliseconds,
        HttpContext context)
    {
        var filenamesIncluded = context.Items[FilenamesIncludedItemKey] is true;
        if (outcome is "success" or "cancelled")
        {
            _logger.LogInformation(
                "Operational audit: Operation: {Operation}; Outcome: {Outcome}; HttpStatus: {HttpStatus}; ElapsedMilliseconds: {ElapsedMilliseconds}; FilenamesIncluded: {FilenamesIncluded}",
                operation, outcome, statusCode, elapsedMilliseconds, filenamesIncluded);
        }
        else if (outcome is "unauthorized" or "invalid-request")
        {
            _logger.LogWarning(
                "Operational audit: Operation: {Operation}; Outcome: {Outcome}; HttpStatus: {HttpStatus}; ElapsedMilliseconds: {ElapsedMilliseconds}; FilenamesIncluded: {FilenamesIncluded}",
                operation, outcome, statusCode, elapsedMilliseconds, filenamesIncluded);
        }
        else
        {
            _logger.LogError(
                "Operational audit: Operation: {Operation}; Outcome: {Outcome}; HttpStatus: {HttpStatus}; ElapsedMilliseconds: {ElapsedMilliseconds}; FilenamesIncluded: {FilenamesIncluded}",
                operation, outcome, statusCode, elapsedMilliseconds, filenamesIncluded);
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
            Log(operation, outcome, statusCode, Stopwatch.GetElapsedTime(startedAt), context.HttpContext);
            return result;
        }
        catch (OperationCanceledException) when (context.HttpContext.RequestAborted.IsCancellationRequested)
        {
            Log(operation, "cancelled", 499, Stopwatch.GetElapsedTime(startedAt), context.HttpContext);
            throw;
        }
        catch
        {
            Log(operation, "failure", StatusCodes.Status500InternalServerError, Stopwatch.GetElapsedTime(startedAt), context.HttpContext);
            throw;
        }
    }
}
