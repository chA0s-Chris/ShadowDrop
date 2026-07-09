// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Files;

using FluentAssertions;
using NUnit.Framework;
using ShadowDrop.Cli.Files;

public sealed class SafeFileNameTests
{
    [TestCase("report.pdf", "report.pdf")]
    [TestCase("dir/report.pdf", "report.pdf")]
    [TestCase("dir\\report.pdf", "report.pdf")]
    [TestCase("a/b\\c/report.pdf", "report.pdf")]
    [TestCase("re:port*.pdf", "re_port_.pdf")]
    [TestCase("report.pdf...", "report.pdf")]
    [TestCase("  report.pdf  ", "report.pdf")]
    [TestCase("CON", "_CON")]
    [TestCase("nul.txt", "_nul.txt")]
    public void TrySanitize_ShouldReduceNameToPortableLeaf(String fileName, String expected)
    {
        SafeFileName.TrySanitize(fileName, out var safeFileName).Should().BeTrue();
        safeFileName.Should().Be(expected);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase(".")]
    [TestCase("..")]
    [TestCase("...")]
    [TestCase("/")]
    [TestCase("dir/")]
    [TestCase("dir\\")]
    [TestCase("dir/..")]
    public void TrySanitize_ShouldReject_WhenNoUsableLeafRemains(String? fileName)
    {
        SafeFileName.TrySanitize(fileName, out var safeFileName).Should().BeFalse();
        safeFileName.Should().BeNull();
    }

    [Test]
    public void TrySanitize_ShouldReplaceControlCharacters()
    {
        SafeFileName.TrySanitize("re\u0007po\u0001rt.bin", out var safeFileName).Should().BeTrue();
        safeFileName.Should().Be("re_po_rt.bin");
    }
}
