// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Uploads;

internal static class RecursiveLinkBoundary
{
    // The kernel gives up after a fixed number of link hops. Matching that keeps a cyclic or deliberately
    // deep arrangement from turning resolution into an unbounded walk, and it bounds the recursion below.
    private const Int32 MaxLinkHops = 40;

    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    private static readonly Char[] PathSeparators = OperatingSystem.IsWindows()
        ? ['\\', '/']
        : ['/'];

    // Junction and link targets are stored in the NT or extended-length namespace and are handed back
    // verbatim, so both spellings must collapse onto the ordinary one before any comparison. The UNC
    // variants have to be tested before their shorter prefixes.
    private static readonly (String Prefix, String Replacement)[] WindowsPathPrefixes =
    [
        (@"\??\UNC\", @"\\"),
        (@"\\?\UNC\", @"\\"),
        (@"\??\", ""),
        (@"\\?\", "")
    ];

    public static Boolean TryResolveDirectoryRoot(String path, out String root)
    {
        var remainingHops = MaxLinkHops;
        if (TryResolvePhysicalPath(path, ref remainingHops, out var resolved) && Directory.Exists(resolved))
        {
            root = resolved;
            return true;
        }

        root = String.Empty;
        return false;
    }

    public static Boolean TryValidateFile(FileInfo file, String root)
    {
        ArgumentNullException.ThrowIfNull(file);

        // FileInfo caches its state, so the refresh is what makes this a fresh look at the file system
        // rather than a replay of whatever discovery saw.
        file.Refresh();
        var remainingHops = MaxLinkHops;
        return TryResolvePhysicalPath(file.FullName, ref remainingHops, out var resolved)
               && File.Exists(resolved)
               && IsChildOf(resolved, root);
    }

    private static Boolean IsChildOf(String path, String root)
    {
        var normalizedRoot = Normalize(root);
        var rootPrefix = Path.EndsInDirectorySeparator(normalizedRoot)
            ? normalizedRoot
            : normalizedRoot + Path.DirectorySeparatorChar;
        return path.StartsWith(rootPrefix, PathComparison);
    }

    private static String Normalize(String path)
    {
        if (!OperatingSystem.IsWindows())
        {
            return Path.TrimEndingDirectorySeparator(path);
        }

        foreach (var (prefix, replacement) in WindowsPathPrefixes)
        {
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                path = replacement + path[prefix.Length..];
                break;
            }
        }

        return Path.TrimEndingDirectorySeparator(path);
    }

    private static String Parent(String path)
    {
        var parent = Path.GetDirectoryName(path);
        // A path root has no parent, and the kernel resolves '..' there to the root itself.
        return String.IsNullOrEmpty(parent) ? path : Normalize(parent);
    }

    private static Boolean TryResolveComponent(String path, ref Int32 remainingHops, out String resolved)
    {
        resolved = path;
        try
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) == 0)
            {
                return true;
            }

            if (remainingHops-- <= 0)
            {
                return false;
            }

            var linkTarget = (attributes & FileAttributes.Directory) != 0
                ? new DirectoryInfo(path).LinkTarget
                : new FileInfo(path).LinkTarget;
            if (String.IsNullOrEmpty(linkTarget))
            {
                return false;
            }

            // A relative target is interpreted against the directory holding the link, which is already
            // physical here. Joining the two as text rather than through Path.GetFullPath leaves any '..'
            // in the target for the walk below to resolve in order, after the links before it.
            var target = Path.IsPathRooted(linkTarget)
                ? linkTarget
                : Parent(path) + Path.DirectorySeparatorChar + linkTarget;
            return TryResolvePhysicalPath(target, ref remainingHops, out resolved);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    // Resolves a path the way the kernel does: one component at a time, following every link that is
    // encountered and re-resolving whatever it points at, including the links in that target's own
    // ancestry. Path.GetFullPath cannot stand in for this. It collapses '..' lexically, which steps back
    // out of the directory a link appears to sit in instead of the one it actually points at, and it
    // leaves the ancestry of a resolved target untouched, so two spellings of the same location would
    // not compare equal.
    private static Boolean TryResolvePhysicalPath(String path, ref Int32 remainingHops, out String resolved)
    {
        resolved = String.Empty;
        var normalizedPath = Normalize(path);
        var pathRoot = Path.GetPathRoot(normalizedPath);
        if (String.IsNullOrEmpty(pathRoot))
        {
            return false;
        }

        var current = Normalize(pathRoot);
        foreach (var segment in normalizedPath[pathRoot.Length..].Split(PathSeparators, StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".")
            {
                continue;
            }

            if (segment == "..")
            {
                current = Parent(current);
                continue;
            }

            if (!TryResolveComponent(Path.Combine(current, segment), ref remainingHops, out current))
            {
                return false;
            }
        }

        resolved = current;
        return true;
    }
}
