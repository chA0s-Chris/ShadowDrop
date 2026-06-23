// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Queues;

using FluentAssertions;
using NUnit.Framework;
using ShadowDrop.Cli.Queues;
using ShadowDrop.Contracts;
using ShadowDrop.Queue;

public sealed class QueueFileBuilderTests
{
    [Test]
    public void Build_ShouldEmbedCredentials_WhenProvided()
    {
        var manifest = Manifest(("11111111-1111-1111-1111-111111111111", "report.txt", 4096));
        var credentials = new QueueCredentials
        {
            ShareKey = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
            DownloadBearerToken = "bearer-xyz"
        };

        var queue = QueueFileBuilder.Build(new("https://shadowdrop.test/"), "token-abc", manifest, credentials);

        queue.Credentials.Should().NotBeNull();
        queue.Credentials!.ShareKey.Should().Be("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef");
        queue.Credentials.DownloadBearerToken.Should().Be("bearer-xyz");
    }

    [Test]
    public void Build_ShouldMapManifestEntries_AndOmitCredentialsForSecretFreeQueue()
    {
        var manifest = Manifest(("11111111-1111-1111-1111-111111111111", "report.txt", 4096));

        var queue = QueueFileBuilder.Build(new("https://shadowdrop.test/"), "token-abc", manifest, null);

        queue.Credentials.Should().BeNull();
        queue.QueueVersion.Should().Be(FormatConstants.QueueVersion);
        var entry = queue.Files.Should().ContainSingle().Subject;
        entry.ShareToken.Should().Be("token-abc");
        entry.ServerUrl.Should().Be("https://shadowdrop.test/");
        entry.FileId.Should().Be("11111111-1111-1111-1111-111111111111");
        entry.FileName.Should().Be("report.txt");
        entry.Length.Should().Be(4096);
        entry.OutputPath.Should().Be("report.txt");
    }

    [TestCase("con.txt", "_con.txt")]
    [TestCase("NUL", "_NUL")]
    [TestCase("file.", "file")]
    [TestCase("trailing. ", "trailing")]
    public void Build_ShouldNormalizeWindowsUnsafeNames(String fileName, String expected)
    {
        var manifest = Manifest(("11111111-1111-1111-1111-111111111111", fileName, 1));

        var queue = QueueFileBuilder.Build(new("https://shadowdrop.test/"), "token", manifest, null);

        queue.Files!.Single().OutputPath.Should().Be(expected);
    }

    [Test]
    public void Build_ShouldResolveDuplicateNamesDeterministically_WithoutIntroducingDirectories()
    {
        var manifest = Manifest(
            ("11111111-1111-1111-1111-111111111111", "report.txt", 1),
            ("22222222-2222-2222-2222-222222222222", "report.txt", 2),
            ("33333333-3333-3333-3333-333333333333", "report.txt", 3));

        var queue = QueueFileBuilder.Build(new("https://shadowdrop.test/"), "token", manifest, null);

        queue.Files!.Select(entry => entry.OutputPath).Should().Equal("report.txt", "report (2).txt", "report (3).txt");
    }

    [Test]
    public void Build_ShouldSanitizeWindowsInvalidCharacters_RegardlessOfHostPlatform()
    {
        var manifest = Manifest(("11111111-1111-1111-1111-111111111111", "a:b*c?.txt", 1));

        var queue = QueueFileBuilder.Build(new("https://shadowdrop.test/"), "token", manifest, null);

        queue.Files!.Single().OutputPath.Should().Be("a_b_c_.txt");
    }

    [Test]
    public void Build_ShouldStripBackslashDirectoryComponents_RegardlessOfHostPlatform()
    {
        var manifest = Manifest(("11111111-1111-1111-1111-111111111111", "dir\\sub\\file.txt", 1));

        var queue = QueueFileBuilder.Build(new("https://shadowdrop.test/"), "token", manifest, null);

        queue.Files!.Single().OutputPath.Should().Be("file.txt");
    }

    [Test]
    public void Build_ShouldStripDirectoryComponentsFromOutputPaths()
    {
        var manifest = Manifest(("11111111-1111-1111-1111-111111111111", "../../etc/passwd", 1));

        var queue = QueueFileBuilder.Build(new("https://shadowdrop.test/"), "token", manifest, null);

        queue.Files!.Single().OutputPath.Should().Be("passwd");
    }

    [Test]
    public void Build_ShouldThrow_WhenManifestIsEmpty()
    {
        var act = () => QueueFileBuilder.Build(new("https://shadowdrop.test/"), "token", new()
        {
            Files = []
        }, null);

        act.Should().Throw<QueueBuildException>();
    }

    [TestCase("..")]
    [TestCase("/")]
    [TestCase("   ")]
    public void Build_ShouldThrow_WhenNameCannotBeSanitized(String fileName)
    {
        var manifest = Manifest(("11111111-1111-1111-1111-111111111111", fileName, 1));

        var act = () => QueueFileBuilder.Build(new("https://shadowdrop.test/"), "token", manifest, null);

        act.Should().Throw<QueueBuildException>();
    }

    [Test]
    public void Build_ShouldTreatCaseOnlyDuplicatesAsCollisions()
    {
        var manifest = Manifest(
            ("11111111-1111-1111-1111-111111111111", "Report.txt", 1),
            ("22222222-2222-2222-2222-222222222222", "report.txt", 2));

        var queue = QueueFileBuilder.Build(new("https://shadowdrop.test/"), "token", manifest, null);

        queue.Files!.Select(entry => entry.OutputPath).Should().Equal("Report.txt", "report (2).txt");
    }

    private static ShareManifestContract Manifest(params (String FileId, String FileName, Int64 Length)[] files) =>
        new()
        {
            Files = files.Select(file => new ShareManifestFileContract
            {
                FileId = file.FileId,
                FileName = file.FileName,
                Length = file.Length,
                KdfSalt = "c2FsdA==",
                AlgorithmId = FormatConstants.Aes256GcmAlgorithmId,
                EncryptionFormatVersion = FormatConstants.EncryptionFormatVersion,
                ChunkSize = 1024,
                ChunkCount = 1
            }).ToArray()
        };
}
