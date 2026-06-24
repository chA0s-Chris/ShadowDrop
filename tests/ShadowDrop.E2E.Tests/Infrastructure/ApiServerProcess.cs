// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Infrastructure;

using System.Diagnostics;
using System.Text;

/// <summary>
/// Starts <c>ShadowDrop.Api</c> as a separate process bound to an isolated loopback port with temporary
/// metadata and storage paths, waits for the <c>/health</c> endpoint, and reliably terminates the process
/// (and captures its output for diagnostics) on disposal.
/// </summary>
internal sealed class ApiServerProcess : IAsyncDisposable
{
    private static readonly TimeSpan HealthTimeout = TimeSpan.FromSeconds(40);

    private readonly Process _process;
    private readonly StringBuilder _standardError;
    private readonly StringBuilder _standardOutput;

    private ApiServerProcess(Process process, Uri baseAddress, String adminToken, StringBuilder standardOutput, StringBuilder standardError)
    {
        _process = process;
        _standardOutput = standardOutput;
        _standardError = standardError;
        BaseAddress = baseAddress;
        AdminToken = adminToken;
    }

    /// <summary>
    /// The bootstrap admin token. The CLI upload endpoints are admin-guarded, so this same value is used as
    /// the CLI upload token; otherwise uploads are rejected with 401.
    /// </summary>
    public String AdminToken { get; }

    /// <summary>The base URL the API is listening on, with a trailing slash for relative URI composition.</summary>
    public Uri BaseAddress { get; }

    public static async Task<ApiServerProcess> StartAsync(ProductArtifacts artifacts, String dataDirectory, CancellationToken cancellationToken)
    {
        var port = NetworkPorts.FindFreeLoopbackPort();
        var baseAddress = new Uri($"http://127.0.0.1:{port}/");
        var adminToken = $"e2e-{Guid.NewGuid():N}";

        var apiDirectory = Path.GetDirectoryName(artifacts.ApiAssemblyPath)
                           ?? throw new InvalidOperationException("The API assembly path has no parent directory.");

        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = apiDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        startInfo.ArgumentList.Add(artifacts.ApiAssemblyPath);
        startInfo.Environment["ASPNETCORE_URLS"] = baseAddress.AbsoluteUri.TrimEnd('/');
        startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
        startInfo.Environment["SHADOWDROP_BOOTSTRAP_ADMIN_TOKEN"] = adminToken;
        startInfo.Environment["ShadowDrop__Metadata__LiteDbPath"] = Path.Combine(dataDirectory, "metadata", "shadowdrop.db");
        startInfo.Environment["ShadowDrop__Storage__LocalRoot"] = Path.Combine(dataDirectory, "storage");
        startInfo.Environment["ShadowDrop__ApiExposure__EnableAdminOperations"] = "true";
        startInfo.Environment["ShadowDrop__ApiExposure__EnablePublicDownloads"] = "true";

        var process = new Process
        {
            StartInfo = startInfo
        };

        var standardOutput = new StringBuilder();
        var standardError = new StringBuilder();
        AttachOutputCapture(process, standardOutput, standardError);

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var server = new ApiServerProcess(process, baseAddress, adminToken, standardOutput, standardError);
        try
        {
            await server.WaitForHealthyAsync(cancellationToken);
            return server;
        }
        catch
        {
            await server.DisposeAsync();
            throw;
        }
    }

    /// <summary>Renders the captured API stdout/stderr so a failing scenario is diagnosable.</summary>
    public String DiagnosticsTail()
    {
        lock (_standardOutput)
        {
            lock (_standardError)
            {
                return
                    $"{Environment.NewLine}--- API STDOUT ---{Environment.NewLine}{_standardOutput}{Environment.NewLine}--- API STDERR ---{Environment.NewLine}{_standardError}";
            }
        }
    }

    private static void AttachOutputCapture(Process process, StringBuilder standardOutput, StringBuilder standardError)
    {
        process.OutputDataReceived += (_, eventArgs) =>
        {
            if (eventArgs.Data is null)
            {
                return;
            }

            lock (standardOutput)
            {
                standardOutput.AppendLine(eventArgs.Data);
            }
        };

        process.ErrorDataReceived += (_, eventArgs) =>
        {
            if (eventArgs.Data is null)
            {
                return;
            }

            lock (standardError)
            {
                standardError.AppendLine(eventArgs.Data);
            }
        };
    }

    private async Task WaitForHealthyAsync(CancellationToken cancellationToken)
    {
        using var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(2)
        };

        var healthUri = new Uri(BaseAddress, "health");
        var deadline = DateTime.UtcNow + HealthTimeout;

        while (DateTime.UtcNow < deadline)
        {
            if (_process.HasExited)
            {
                throw new InvalidOperationException(
                    $"The API process exited with code {_process.ExitCode} before becoming healthy.{DiagnosticsTail()}");
            }

            try
            {
                using var response = await client.GetAsync(healthUri, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
                // The listener is not accepting connections yet; retry until the deadline.
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // The per-request timeout elapsed; retry until the deadline.
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
        }

        throw new TimeoutException($"The API did not report healthy at '{healthUri}' within {HealthTimeout}.{DiagnosticsTail()}");
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(true);
                await _process.WaitForExitAsync();
            }
        }
        catch (InvalidOperationException)
        {
            // The process already exited; nothing further to terminate.
        }
        finally
        {
            _process.Dispose();
        }
    }
}
