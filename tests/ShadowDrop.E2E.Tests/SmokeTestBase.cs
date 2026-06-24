// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests;

using FluentAssertions;
using ShadowDrop.Tests.Infrastructure;

/// <summary>
/// Shared helpers for the real end-to-end smoke tests: access to the prebuilt artifacts, deterministic input
/// files, CLI output parsing, and byte-for-byte file comparison.
/// </summary>
public abstract class SmokeTestBase
{
    internal static ProductArtifacts Artifacts => ProductArtifactsFixture.Artifacts;

    /// <summary>Returns the value after the first stdout line that starts with <paramref name="prefix"/>.</summary>
    internal static String RequireOutputValue(ProcessResult result, String prefix)
    {
        var value = result.StandardOutput
                          .Split('\n')
                          .Select(static line => line.TrimEnd('\r'))
                          .Where(line => line.StartsWith(prefix, StringComparison.Ordinal))
                          .Select(line => line[prefix.Length..])
                          .FirstOrDefault();

        value.Should().NotBeNull($"the CLI output should contain a '{prefix}' line.{Environment.NewLine}{result.Describe()}");
        return value!;
    }

    protected static void AssertFilesEqual(FileInfo expected, String actualPath)
    {
        File.Exists(actualPath).Should().BeTrue($"'{actualPath}' should have been written.");
        File.ReadAllBytes(actualPath).Should().Equal(File.ReadAllBytes(expected.FullName),
                                                     $"'{Path.GetFileName(actualPath)}' should match the original byte-for-byte.");
    }

    protected static FileInfo CreateInputFile(String directory, String name, Int32 seed, Int32 sizeInBytes = 8192)
    {
        var bytes = new Byte[sizeInBytes];
        new Random(seed).NextBytes(bytes);

        var path = Path.Combine(directory, name);
        File.WriteAllBytes(path, bytes);
        return new(path);
    }
}
