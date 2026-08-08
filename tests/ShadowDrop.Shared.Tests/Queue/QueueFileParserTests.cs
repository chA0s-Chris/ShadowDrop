// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Queue;

using FluentAssertions;
using NUnit.Framework;
using ShadowDrop.Contracts;
using ShadowDrop.Queue;
using System.Text.Json;

public sealed class QueueFileParserTests
{
    private const String UnsupportedQueueVersionMessage =
        "The queueVersion value must be '2.0'. Queue files created by earlier ShadowDrop versions are not supported; "
        + "recreate the queue with 'shadowdrop queue create' or 'shadowdrop upload --queue-out'.";

    [Test]
    public void Constants_ShouldExposeStableSharedValues()
    {
        DownloadKeyConstants.HeaderName.Should().Be("ShadowDrop-Key");
        DownloadKeyConstants.QueryParameterName.Should().Be("sd-key");
        CliConfigPathConstants.ConfigDirectoryName.Should().Be(".config");
        CliConfigPathConstants.ApplicationDirectoryName.Should().Be("shadowdrop");
        CliConfigPathConstants.FileName.Should().Be("config.json");
        FormatConstants.ShadowDropVersion.Should().Be("1.0");
        FormatConstants.QueueVersion.Should().Be("2.0");
        FormatConstants.EncryptionFormatVersion.Should().Be("1.0");
        FormatConstants.Aes256GcmAlgorithmId.Should().Be("aes-256-gcm");
    }

    [Test]
    public void Deserialize_ShouldRoundTripQueueFile()
    {
        var expected = CreateValidQueueFile();
        var json = QueueFileParser.Serialize(expected);

        var actual = QueueFileParser.Deserialize(json);

        actual.Should().BeEquivalentTo(expected);
    }

    [Test]
    public void Deserialize_ShouldSetPlaintextSha256ToNull_WhenItIsOmitted()
    {
        var json = CreateQueueJson("""
                                   {
                                     "fileId": "file-1",
                                     "fileName": "report.txt",
                                     "length": 4096,
                                     "outputPath": "downloads/report.txt"
                                   }
                                   """);

        var queueFile = QueueFileParser.Deserialize(json);

        queueFile.Files.Should().ContainSingle();
        queueFile.Files[0].PlaintextSha256.Should().BeNull();
    }

    [Test]
    public void Parse_ShouldAcceptEntryWithoutOutputPath()
    {
        var json = CreateQueueJson("""
                                   {
                                     "fileId": "file-1",
                                     "fileName": "report.txt",
                                     "length": 4096
                                   }
                                   """);

        var queueFile = QueueFileParser.Parse(json);

        queueFile.Files.Should().ContainSingle();
        QueueOutputPath.Resolve(queueFile.Files[0]).Should().Be("report.txt");
    }

    [Test]
    public void Parse_ShouldAcceptFileNameWithSeparators_WhenExplicitOutputPathIsPresent()
    {
        var json = CreateQueueJson("""
                                   {
                                     "fileId": "file-1",
                                     "fileName": "sub/report.txt",
                                     "length": 4096,
                                     "outputPath": "sub/report.txt"
                                   }
                                   """);

        var queueFile = QueueFileParser.Parse(json);

        QueueOutputPath.Resolve(queueFile.Files!.Single()).Should().Be("sub/report.txt");
    }

    [TestCase("ftp://example.com/upload")]
    [TestCase("file:///tmp/report.txt")]
    public void Parse_ShouldRejectAbsoluteServerUrlWithNonHttpScheme(String serverUrl)
    {
        var json = QueueFileParser.Serialize(CreateValidQueueFile() with
        {
            ServerUrl = serverUrl
        });

        var act = () => QueueFileParser.Parse(json);

        act.Should()
           .Throw<QueueFileValidationException>()
           .Which.Errors.Should().ContainSingle(error =>
                                                    error.Path == "serverUrl" &&
                                                    error.Message == "The serverUrl value must be an absolute HTTP or HTTPS URL.");
    }

    [Test]
    public void Parse_ShouldRejectAncestorConflictBetweenFileAndDirectory()
    {
        var queueFile = CreateQueueFileWithOutputPaths("docs", "docs/report.txt");

        var errors = QueueFileParser.Validate(queueFile);

        errors.Should().ContainSingle(error =>
                                          error.Path == "files[0].outputPath" &&
                                          error.Message == "The output path 'docs' is also used as a directory by another file entry.");
    }

    [Test]
    public void Parse_ShouldRejectAncestorConflict_WhenTheDirectoryIsDeclaredFirst()
    {
        var queueFile = CreateQueueFileWithOutputPaths("docs/report.txt", "DOCS");

        var errors = QueueFileParser.Validate(queueFile);

        errors.Should().ContainSingle(error =>
                                          error.Path == "files[1].outputPath" &&
                                          error.Message == "The output path 'DOCS' is also used as a directory by another file entry.");
    }

    [Test]
    public void Parse_ShouldRejectDuplicateOutputPathsThatDifferOnlyByCase()
    {
        var queueFile = CreateQueueFileWithOutputPaths("docs/report.txt", "docs/REPORT.TXT");

        var errors = QueueFileParser.Validate(queueFile);

        errors.Should().ContainSingle(error =>
                                          error.Path == "files[1].outputPath" &&
                                          error.Message == "The output path 'docs/REPORT.TXT' is used by more than one file entry.");
    }

    [Test]
    public void Parse_ShouldRejectFileEntryWithoutLength()
    {
        var json = CreateQueueJson("""
                                   {
                                     "fileId": "file-1",
                                     "fileName": "report.txt",
                                     "outputPath": "downloads/report.txt"
                                   }
                                   """);

        var act = () => QueueFileParser.Parse(json);

        act.Should()
           .Throw<QueueFileValidationException>()
           .Which.Errors.Should().ContainSingle(error =>
                                                    error.Path == "files[0].length" &&
                                                    error.Message == "The length value is required.");
    }

    [Test]
    public void Parse_ShouldRejectInvalidQueueFile()
    {
        const String json = """
                            {
                              "shadowDrop": "2.0",
                              "queueVersion": "2.0",
                              "serverUrl": "notaurl",
                              "shareToken": "",
                              "files": [
                                {
                                  "fileId": "",
                                  "fileName": "",
                                  "length": -1,
                                  "outputPath": "",
                                  "plaintextSha256": "nope"
                                }
                              ]
                            }
                            """;

        var act = () => QueueFileParser.Parse(json);

        act.Should()
           .Throw<QueueFileValidationException>()
           .Which.Errors.Should().BeEquivalentTo(
               [
                   new("shadowDrop", "The shadowDrop value must be '1.0'."),
                   new("serverUrl", "The serverUrl value must be an absolute HTTP or HTTPS URL."),
                   new("shareToken", "The shareToken value is required."),
                   new("files[0].fileId", "The fileId value is required."),
                   new("files[0].fileName", "The fileName value is required."),
                   new("files[0].length", "The file length must be zero or greater."),
                   new("files[0].outputPath", "The outputPath value is required."),
                   new QueueFileValidationError("files[0].plaintextSha256",
                                                "The plaintextSha256 value must be a 64-character lowercase hexadecimal SHA-256 digest.")
               ],
               options => options.WithoutStrictOrdering());
    }

    [Test]
    public void Parse_ShouldRejectPlaintextSha256WithTrailingNewline()
    {
        var queueFile = CreateValidQueueFile() with
        {
            Files =
            [
                CreateValidQueueFile().Files!.Single() with
                {
                    PlaintextSha256 = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef\n"
                }
            ]
        };
        var json = QueueFileParser.Serialize(queueFile);

        var act = () => QueueFileParser.Parse(json);

        act.Should()
           .Throw<QueueFileValidationException>()
           .Which.Errors.Should().ContainSingle(error =>
                                                    error.Path == "files[0].plaintextSha256" &&
                                                    error.Message == "The plaintextSha256 value must be a 64-character lowercase hexadecimal SHA-256 digest.");
    }

    [TestCase("https://example.com/upload?sd-key=secret")]
    [TestCase("https://example.com/upload?foo=bar")]
    [TestCase("https://example.com/upload#fragment")]
    public void Parse_ShouldRejectServerUrlWithQueryOrFragment(String serverUrl)
    {
        var json = QueueFileParser.Serialize(CreateValidQueueFile() with
        {
            ServerUrl = serverUrl
        });

        var act = () => QueueFileParser.Parse(json);

        act.Should()
           .Throw<QueueFileValidationException>()
           .Which.Errors.Should().ContainSingle(error =>
                                                    error.Path == "serverUrl" &&
                                                    error.Message == "The serverUrl value must not include query string or fragment components.");
    }

    [Test]
    public void Parse_ShouldRejectServerUrlWithUserInfo()
    {
        var json = QueueFileParser.Serialize(CreateValidQueueFile() with
        {
            ServerUrl = "https://user:pass@example.com/upload"
        });

        var act = () => QueueFileParser.Parse(json);

        act.Should()
           .Throw<QueueFileValidationException>()
           .Which.Errors.Should().ContainSingle(error =>
                                                    error.Path == "serverUrl" &&
                                                    error.Message == "The serverUrl value must not include user information.");
    }

    // A server-announced name is metadata, not a destination, so it may not carry path semantics on its own.
    [TestCase("sub/report.txt", "The fileName value must not contain directory separators; carry a nested destination in outputPath instead.")]
    [TestCase(@"sub\report.txt", "The fileName value must use '/' as its directory separator.")]
    [TestCase("/report.txt", "The fileName value must be a relative path.")]
    public void Parse_ShouldRejectUnsafeFileName_WhenOutputPathIsOmitted(String fileName, String expectedMessage)
    {
        var queueFile = CreateValidQueueFile() with
        {
            Files =
            [
                CreateValidQueueFile().Files!.Single() with
                {
                    FileName = fileName,
                    OutputPath = null
                }
            ]
        };
        var json = QueueFileParser.Serialize(queueFile);

        var act = () => QueueFileParser.Parse(json);

        act.Should()
           .Throw<QueueFileValidationException>()
           .Which.Errors.Should().ContainSingle(error => error.Path == "files[0].fileName" && error.Message == expectedMessage);
    }

    // Both Windows and POSIX path forms are rejected regardless of the host the tests run on, so a queue written on
    // one platform cannot smuggle an unsafe destination onto another.
    [TestCase("/x", "The outputPath value must be a relative path.")]
    [TestCase("//server/share", "The outputPath value must be a relative path.")]
    [TestCase(@"sub\report.txt", "The outputPath value must use '/' as its directory separator.")]
    [TestCase(@"C:\x", "The outputPath value must use '/' as its directory separator.")]
    [TestCase(@"\\server\share\x", "The outputPath value must use '/' as its directory separator.")]
    [TestCase("C:", "The outputPath value must not contain the characters <>:\"|?* or control characters.")]
    [TestCase("C:report.txt", "The outputPath value must not contain the characters <>:\"|?* or control characters.")]
    [TestCase("C:/x", "The outputPath value must not contain the characters <>:\"|?* or control characters.")]
    [TestCase("../report.txt", "The outputPath value must not contain '.' or '..' path segments.")]
    [TestCase("docs/../../report.txt", "The outputPath value must not contain '.' or '..' path segments.")]
    [TestCase("./report.txt", "The outputPath value must not contain '.' or '..' path segments.")]
    [TestCase("docs//report.txt", "The outputPath value must not contain empty path segments.")]
    [TestCase("docs/report.txt/", "The outputPath value must not contain empty path segments.")]
    public void Parse_ShouldRejectUnsafeOutputPathForms(String outputPath, String expectedMessage)
    {
        var queueFile = CreateValidQueueFile() with
        {
            Files =
            [
                CreateValidQueueFile().Files!.Single() with
                {
                    OutputPath = outputPath
                }
            ]
        };
        var json = QueueFileParser.Serialize(queueFile);

        var act = () => QueueFileParser.Parse(json);

        act.Should()
           .Throw<QueueFileValidationException>()
           .Which.Errors.Should().ContainSingle(error => error.Path == "files[0].outputPath" && error.Message == expectedMessage);
    }

    // The queue format break is not migrated, so both the superseded version and an unknown one must say the same
    // recreate-the-queue thing instead of failing with a bare structural mismatch.
    [TestCase("1.0")]
    [TestCase("3.0")]
    [TestCase("2")]
    public void Parse_ShouldRejectUnsupportedQueueVersionWithRecreationGuidance(String queueVersion)
    {
        var json = QueueFileParser.Serialize(CreateValidQueueFile() with
        {
            QueueVersion = queueVersion
        });

        var act = () => QueueFileParser.Parse(json);

        act.Should()
           .Throw<QueueFileValidationException>()
           .Which.Errors.Should().ContainSingle(error =>
                                                    error.Path == "queueVersion" &&
                                                    error.Message == UnsupportedQueueVersionMessage);
    }

    [Test]
    public void Serialize_ShouldOmitCredentials_WhenSecretFree()
    {
        var json = QueueFileParser.Serialize(CreateValidQueueFile());

        using var document = JsonDocument.Parse(json);
        document.RootElement.TryGetProperty("credentials", out _).Should().BeFalse();
    }

    [Test]
    public void Serialize_ShouldOmitOptionalPlaintextSha256_WhenItIsNull()
    {
        var queueFile = CreateValidQueueFile() with
        {
            Files =
            [
                new()
                {
                    FileId = "file-1",
                    FileName = "report.txt",
                    Length = 4096,
                    OutputPath = "downloads/report.txt"
                }
            ]
        };

        var json = QueueFileParser.Serialize(queueFile);
        using var document = JsonDocument.Parse(json);
        var entry = document.RootElement.GetProperty("files")[0];

        entry.TryGetProperty("plaintextSha256", out _).Should().BeFalse();
    }

    [Test]
    public void Serialize_ShouldOmitOutputPath_WhenItIsNull()
    {
        var queueFile = CreateValidQueueFile() with
        {
            Files =
            [
                CreateValidQueueFile().Files!.Single() with
                {
                    OutputPath = null
                }
            ]
        };

        var json = QueueFileParser.Serialize(queueFile);
        using var document = JsonDocument.Parse(json);

        document.RootElement.GetProperty("files")[0].TryGetProperty("outputPath", out _).Should().BeFalse();
    }

    [Test]
    public void Serialize_ShouldRoundTripEmbeddedCredentials()
    {
        var queueFile = CreateValidQueueFile() with
        {
            Credentials = new()
            {
                ShareKey = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
                DownloadBearerToken = "bearer"
            }
        };

        var roundTripped = QueueFileParser.Deserialize(QueueFileParser.Serialize(queueFile));

        roundTripped.Credentials.Should().NotBeNull();
        roundTripped.Credentials.ShareKey.Should().Be("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef");
        roundTripped.Credentials.DownloadBearerToken.Should().Be("bearer");
    }

    [Test]
    public void Serialize_ShouldUseExactQueuePropertyNames()
    {
        var queueFile = CreateValidQueueFile();

        var json = QueueFileParser.Serialize(queueFile);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        root.EnumerateObject().Select(property => property.Name).Should()
            .Equal("shadowDrop", "queueVersion", "serverUrl", "shareToken", "files");

        var entry = root.GetProperty("files")[0];
        entry.EnumerateObject().Select(property => property.Name).Should()
             .Equal("fileId", "fileName", "length", "outputPath", "plaintextSha256");
    }

    [Test]
    public void Validate_ShouldRejectCredentials_WhenShareKeyMalformed()
    {
        var queueFile = CreateValidQueueFile() with
        {
            Credentials = new()
            {
                ShareKey = "not-a-valid-hex-key"
            }
        };

        var errors = QueueFileParser.Validate(queueFile);

        errors.Should().Contain(error => error.Path == "credentials.shareKey"
                                         && error.Message == "The shareKey value must be 64-character lowercase hexadecimal share-key material.");
    }

    [Test]
    public void Validate_ShouldRejectCredentials_WhenShareKeyMissing()
    {
        var queueFile = CreateValidQueueFile() with
        {
            Credentials = new()
            {
                ShareKey = null
            }
        };

        var errors = QueueFileParser.Validate(queueFile);

        errors.Should().Contain(error => error.Path == "credentials.shareKey" && error.Message == "The shareKey value is required.");
    }

    [Test]
    public void Validate_ShouldRejectMissingRequiredFieldsAndEmptyFiles()
    {
        var queueFile = new QueueFile
        {
            ShadowDrop = null,
            QueueVersion = null,
            ServerUrl = null,
            ShareToken = null,
            Files = []
        };

        var errors = QueueFileParser.Validate(queueFile);

        errors.Should().BeEquivalentTo(
            [
                new("shadowDrop", "The shadowDrop value is required."),
                new("queueVersion", "The queueVersion value is required."),
                new("serverUrl", "The serverUrl value is required."),
                new("shareToken", "The shareToken value is required."),
                new QueueFileValidationError("files", "The files collection must contain at least one entry.")
            ],
            options => options.WithoutStrictOrdering());
    }

    private static QueueFile CreateQueueFileWithOutputPaths(params String[] outputPaths) =>
        CreateValidQueueFile() with
        {
            Files = outputPaths.Select(static (outputPath, index) => new QueueFileEntry
                               {
                                   FileId = $"file-{index}",
                                   FileName = "report.txt",
                                   Length = 4096,
                                   OutputPath = outputPath
                               })
                               .ToArray()
        };

    private static String CreateQueueJson(String fileEntryJson) =>
        $$"""
          {
            "shadowDrop": "1.0",
            "queueVersion": "2.0",
            "serverUrl": "https://example.com",
            "shareToken": "share-123",
            "files": [
              {{fileEntryJson}}
            ]
          }
          """;

    private static QueueFile CreateValidQueueFile() =>
        new()
        {
            ShadowDrop = FormatConstants.ShadowDropVersion,
            QueueVersion = FormatConstants.QueueVersion,
            ServerUrl = "https://example.com",
            ShareToken = "share-123",
            Files =
            [
                new()
                {
                    FileId = "file-1",
                    FileName = "report.txt",
                    Length = 4096,
                    OutputPath = "downloads/report.txt",
                    PlaintextSha256 = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"
                }
            ]
        };
}
