// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.HealthProbe;

internal static class HealthProbe
{
    internal const Int32 HealthyExitCode = 0;
    internal const Int32 InvalidArgumentsExitCode = 2;
    internal const Int32 UnhealthyExitCode = 1;

    internal static async Task<Int32> RunAsync(HttpClient client,
                                               Uri endpoint,
                                               TimeSpan timeout,
                                               CancellationToken cancellationToken)
    {
        using var timeoutCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCancellation.CancelAfter(timeout);

        try
        {
            using var response = await client.GetAsync(endpoint, HttpCompletionOption.ResponseHeadersRead, timeoutCancellation.Token);
            return response.IsSuccessStatusCode ? HealthyExitCode : UnhealthyExitCode;
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException)
        {
            return UnhealthyExitCode;
        }
    }
}
