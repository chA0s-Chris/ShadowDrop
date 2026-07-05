// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Infrastructure;

/// <summary>
/// Builds the API and CLI projects into isolated output directories so the smoke tests can execute the real
/// shipped entrypoints as separate processes. The build happens once per test run (see
/// <see cref="ProductArtifactsFixture"/>) and is cleaned up when the run finishes.
/// </summary>
internal sealed class ProductArtifacts : IDisposable
{
    // Match the configuration the tests themselves were built with so the artifact build reuses the existing
    // incremental compilation outputs instead of forcing a second configuration to compile from scratch.
    public const String BuildConfiguration =
#if DEBUG
        "Debug";
#else
        "Release";
#endif

    private readonly TempWorkspace _workspace;

    private ProductArtifacts(TempWorkspace workspace, String apiAssemblyPath, String cliAssemblyPath)
    {
        _workspace = workspace;
        ApiAssemblyPath = apiAssemblyPath;
        CliAssemblyPath = cliAssemblyPath;
    }

    public String ApiAssemblyPath { get; }

    public String CliAssemblyPath { get; }

    public static async Task<ProductArtifacts> BuildAsync(CancellationToken cancellationToken)
    {
        var workspace = TempWorkspace.Create("shadowdrop-e2e-artifacts");
        try
        {
            var apiOutput = workspace.CreateSubdirectory("api");
            var cliOutput = workspace.CreateSubdirectory("cli");

            await BuildProjectAsync(RepositoryLayout.ApiProjectPath, apiOutput, cancellationToken);
            await BuildProjectAsync(RepositoryLayout.CliProjectPath, cliOutput, cancellationToken);

            var apiAssemblyPath = RequireAssembly(apiOutput, "ShadowDrop.Api.dll");
            // The CLI assembly is named `shadowdrop` (see <AssemblyName> in ShadowDrop.Cli.csproj), not the project name.
            var cliAssemblyPath = RequireAssembly(cliOutput, "shadowdrop.dll");

            return new(workspace, apiAssemblyPath, cliAssemblyPath);
        }
        catch
        {
            workspace.Dispose();
            throw;
        }
    }

    public void Dispose() => _workspace.Dispose();

    private static async Task BuildProjectAsync(String projectPath, String outputDirectory, CancellationToken cancellationToken)
    {
        var result = await ProcessRunner.RunAsync(
            "dotnet",
            ["build", projectPath, "--configuration", BuildConfiguration, "--output", outputDirectory, "--nologo", "--verbosity", "quiet"],
            RepositoryLayout.RepositoryRoot,
            timeout: TimeSpan.FromMinutes(5),
            cancellationToken: cancellationToken);

        if (!result.Succeeded)
        {
            throw new InvalidOperationException($"Building '{projectPath}' failed.{Environment.NewLine}{result.Describe()}");
        }
    }

    private static String RequireAssembly(String outputDirectory, String assemblyName)
    {
        var assemblyPath = Path.Combine(outputDirectory, assemblyName);
        if (!File.Exists(assemblyPath))
        {
            throw new InvalidOperationException($"Expected build output '{assemblyPath}' was not produced.");
        }

        return assemblyPath;
    }
}
