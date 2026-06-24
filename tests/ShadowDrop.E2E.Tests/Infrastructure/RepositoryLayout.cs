// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Infrastructure;

/// <summary>
/// Locates the repository root and the product project files so the smoke tests can build and run the real
/// API and CLI artifacts without taking a compile-time dependency on those projects.
/// </summary>
internal static class RepositoryLayout
{
    public static String ApiProjectPath => Path.Combine(RepositoryRoot, "src", "ShadowDrop.Api", "ShadowDrop.Api.csproj");

    public static String CliProjectPath => Path.Combine(RepositoryRoot, "src", "ShadowDrop.Cli", "ShadowDrop.Cli.csproj");

    public static String RepositoryRoot { get; } = LocateRepositoryRoot();

    private static String LocateRepositoryRoot()
    {
        // Walk up from the test assembly location until the solution marker is found; this keeps the tests
        // runnable from both `dotnet test` and the Nuke E2E target regardless of the build output layout.
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ShadowDrop.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"Could not locate the repository root (ShadowDrop.slnx) starting from '{AppContext.BaseDirectory}'.");
    }
}
