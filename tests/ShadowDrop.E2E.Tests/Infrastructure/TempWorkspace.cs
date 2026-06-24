// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Infrastructure;

/// <summary>
/// An isolated temporary directory that is recursively deleted on disposal so a smoke test leaves no files,
/// directories, or process state behind, even when a scenario fails.
/// </summary>
internal sealed class TempWorkspace : IDisposable
{
    private TempWorkspace(String path) => Path = path;

    public String Path { get; }

    public static TempWorkspace Create(String prefix)
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return new(path);
    }

    public String CreateSubdirectory(String name)
    {
        var path = System.IO.Path.Combine(Path, name);
        Directory.CreateDirectory(path);
        return path;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, true);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup: a transient lock should never fail the test it belongs to.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort cleanup: leftover owner-only artifacts should never fail the test.
        }
    }
}
