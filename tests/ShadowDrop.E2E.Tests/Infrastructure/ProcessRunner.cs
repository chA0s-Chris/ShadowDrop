// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Infrastructure;

using System.Diagnostics;
using System.Text;

/// <summary>
/// The captured outcome of a finished child process.
/// </summary>
internal sealed record ProcessResult(Int32 ExitCode, String StandardOutput, String StandardError)
{
    public Boolean Succeeded => ExitCode == 0;

    public String Describe() =>
        $"Exit code: {ExitCode}{Environment.NewLine}--- STDOUT ---{Environment.NewLine}{StandardOutput}{Environment.NewLine}--- STDERR ---{Environment.NewLine}{StandardError}";
}

/// <summary>
/// Runs a child process to completion, capturing stdout, stderr, and the exit code. Used to drive the real
/// CLI and <c>curl</c> entrypoints as separate OS processes.
/// </summary>
internal static class ProcessRunner
{
    public static async Task<ProcessResult> RunAsync(String fileName,
                                                     IReadOnlyList<String> arguments,
                                                     String workingDirectory,
                                                     IReadOnlyDictionary<String, String?>? environment = null,
                                                     TimeSpan? timeout = null,
                                                     CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        ApplyEnvironment(startInfo, environment);

        using var process = new Process
        {
            StartInfo = startInfo
        };

        var standardOutput = new StringBuilder();
        var standardError = new StringBuilder();
        AttachOutputCapture(process, standardOutput, standardError);

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (timeout is { } limit)
        {
            timeoutSource.CancelAfter(limit);
        }

        try
        {
            await process.WaitForExitAsync(timeoutSource.Token);
        }
        catch (OperationCanceledException) when (timeout is not null && !cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            throw new TimeoutException($"Process '{fileName}' did not exit within {timeout}.");
        }

        // Block briefly so the asynchronous stdout/stderr readers flush before the buffers are read.
        process.WaitForExit();

        return new(process.ExitCode, standardOutput.ToString(), standardError.ToString());
    }

    public static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(true);
            }
        }
        catch (InvalidOperationException)
        {
            // The process already exited between the check and the kill; nothing to clean up.
        }
    }

    private static void ApplyEnvironment(ProcessStartInfo startInfo, IReadOnlyDictionary<String, String?>? environment)
    {
        if (environment is null)
        {
            return;
        }

        foreach (var (key, value) in environment)
        {
            if (value is null)
            {
                startInfo.Environment.Remove(key);
            }
            else
            {
                startInfo.Environment[key] = value;
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
}
