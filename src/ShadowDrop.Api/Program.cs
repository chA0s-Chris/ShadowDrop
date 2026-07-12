// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Api;

using Serilog;
using ShadowDrop.Api.CompositionRoot;

public class Program
{
    public static async Task<Int32> Main(String[] args)
    {
        Log.Logger = Logging.CreateBootstrapLogger();

        try
        {
            await using var app = WebApplication
                                  .CreateBuilder(args)
                                  .ConfigureServices(Log.Logger)
                                  .Build()
                                  .ConfigureMiddleware(Log.Logger);

            // The host's console lifetime only takes over Ctrl-C handling once RunAsync starts, so wire the
            // startup phase to its own cancellation source to keep a slow MongoDB connect interruptible.
            using var startupCancellation = new CancellationTokenSource();
            ConsoleCancelEventHandler cancelStartup = (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                // ReSharper disable once AccessToDisposedClosure
                startupCancellation.Cancel();
            };
            Console.CancelKeyPress += cancelStartup;
            try
            {
                await app.PrepareStartupAsync(Log.Logger, startupCancellation.Token);
            }
            finally
            {
                Console.CancelKeyPress -= cancelStartup;
            }

            await app.RunAsync();
            return 0;
        }
        catch (OperationCanceledException e)
        {
            Log.Information(e, "The host was stopped");
            return 0;
        }
        catch (Exception e)
        {
            Log.Error(e, "The host terminated unexpectedly");
            return 1;
        }
        finally
        {
            await Log.CloseAndFlushAsync();
        }
    }
}
