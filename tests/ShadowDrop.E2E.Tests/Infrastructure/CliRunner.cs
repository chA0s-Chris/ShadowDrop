// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Infrastructure;

/// <summary>
/// Runs the built CLI as a separate process via the dotnet host, capturing stdout, stderr, and exit code.
/// </summary>
internal static class CliRunner
{
    private static readonly TimeSpan CliTimeout = TimeSpan.FromMinutes(2);

    public static Task<ProcessResult> RunAsync(ProductArtifacts artifacts,
                                               IReadOnlyList<String> arguments,
                                               String workingDirectory,
                                               IReadOnlyDictionary<String, String?>? environment = null,
                                               CancellationToken cancellationToken = default)
    {
        var hostArguments = new List<String>(arguments.Count + 1)
        {
            artifacts.CliAssemblyPath
        };
        hostArguments.AddRange(arguments);

        return ProcessRunner.RunAsync("dotnet", hostArguments, workingDirectory, environment, CliTimeout, cancellationToken);
    }
}
