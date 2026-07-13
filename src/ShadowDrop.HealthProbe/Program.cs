// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.HealthProbe;

public static class Program
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(5);

    public static async Task<Int32> Main(String[] args)
    {
        if (args.Length != 1 ||
            !Uri.TryCreate(args[0], UriKind.Absolute, out var endpoint) ||
            endpoint.Scheme is not ("http" or "https"))
        {
            await Console.Error.WriteLineAsync("Usage: dotnet ShadowDrop.HealthProbe.dll <http-or-https-url>");
            return HealthProbe.InvalidArgumentsExitCode;
        }

        using var client = new HttpClient();
        client.Timeout = Timeout.InfiniteTimeSpan;
        return await HealthProbe.RunAsync(client, endpoint, ProbeTimeout, CancellationToken.None);
    }
}
