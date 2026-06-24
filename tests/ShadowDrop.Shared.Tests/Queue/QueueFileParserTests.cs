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
    [Test]
    public void Constants_ShouldExposeStableSharedValues()
    {
        DownloadKeyConstants.HeaderName.Should().Be("ShadowDrop-Key");
        DownloadKeyConstants.QueryParameterName.Should().Be("sd-key");
        CliConfigPathConstants.ConfigDirectoryName.Should().Be(".config");
        CliConfigPathConstants.ApplicationDirectoryName.Should().Be("shadowdrop");
        CliConfigPathConstants.FileName.Should().Be("config.json");
        FormatConstants.ShadowDropVersion.Should().Be("1.0");
        FormatConstants.QueueVersion.Should().Be("1.0");
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
        const String json = """
                            {
                              "shadowDrop": "1.0",
                              "queueVersion": "1.0",
                              "files": [
                                {
                                  "serverUrl": "https://example.com",
                                  "shareToken": "share-123",
                                  "fileId": "file-1",
                                  "fileName": "report.txt",
                                  "length": 4096,
                                  "outputPath": "downloads/report.txt"
                                }
                              ]
                            }
                            """;

        var queueFile = QueueFileParser.Deserialize(json);

        queueFile.Files.Should().ContainSingle();
        queueFile.Files[0].PlaintextSha256.Should().BeNull();
    }

    [TestCase("ftp://example.com/upload")]
    [TestCase("file:///tmp/report.txt")]
    public void Parse_ShouldRejectAbsoluteTargetWithNonHttpScheme(String serverUrl)
    {
        var queueFile = CreateValidQueueFile() with
        {
            Files =
            [
                CreateValidQueueFile().Files!.Single() with
                {
                    ServerUrl = serverUrl
                }
            ]
        };
        var json = QueueFileParser.Serialize(queueFile);

        var act = () => QueueFileParser.Parse(json);

        act.Should()
           .Throw<QueueFileValidationException>()
           .Which.Errors.Should().ContainSingle(error =>
                                                    error.Path == "files[0].serverUrl" &&
                                                    error.Message == "The serverUrl value must be an absolute HTTP or HTTPS URL.");
    }

    [Test]
    public void Parse_ShouldRejectFileEntryWithoutLength()
    {
        const String json = """
                            {
                              "shadowDrop": "1.0",
                              "queueVersion": "1.0",
                              "files": [
                                {
                                  "serverUrl": "https://example.com",
                                  "shareToken": "share-123",
                                  "fileId": "file-1",
                                  "fileName": "report.txt",
                                  "outputPath": "downloads/report.txt"
                                }
                              ]
                            }
                            """;

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
                              "queueVersion": "1.0",
                              "files": [
                                {
                                  "serverUrl": "notaurl",
                                  "shareToken": "",
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
                   new("files[0].serverUrl", "The serverUrl value must be an absolute HTTP or HTTPS URL."),
                   new("files[0].shareToken", "The shareToken value is required."),
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
        var file = CreateValidQueueFile().Files!.Single();
        var queueFile = CreateValidQueueFile() with
        {
            Files =
            [
                file with
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

    [TestCase("/x")]
    [TestCase("C:")]
    [TestCase("C:report.txt")]
    [TestCase(@"C:\x")]
    [TestCase("C:/x")]
    [TestCase(@"\\server\share\x")]
    public void Parse_ShouldRejectPortableAbsoluteOutputPathForms(String outputPath)
    {
        var file = CreateValidQueueFile().Files!.Single();
        var queueFile = CreateValidQueueFile() with
        {
            Files =
            [
                file with
                {
                    OutputPath = outputPath
                }
            ]
        };
        var json = QueueFileParser.Serialize(queueFile);

        var act = () => QueueFileParser.Parse(json);

        act.Should()
           .Throw<QueueFileValidationException>()
           .Which.Errors.Should().ContainSingle(error =>
                                                    error.Path == "files[0].outputPath" &&
                                                    error.Message == "The outputPath value must be a relative path.");
    }

    [Test]
    public void Parse_ShouldRejectQueueFileWithQueueVersionMismatch()
    {
        var queueFile = CreateValidQueueFile() with
        {
            QueueVersion = "2.0"
        };
        var json = QueueFileParser.Serialize(queueFile);

        var act = () => QueueFileParser.Parse(json);

        act.Should()
           .Throw<QueueFileValidationException>()
           .Which.Errors.Should().ContainSingle(error =>
                                                    error.Path == "queueVersion" &&
                                                    error.Message == "The queueVersion value must be '1.0'.");
    }

    [TestCase("https://example.com/upload?sd-key=secret")]
    [TestCase("https://example.com/upload?foo=bar")]
    [TestCase("https://example.com/upload#fragment")]
    public void Parse_ShouldRejectTargetWithQueryOrFragment(String serverUrl)
    {
        var queueFile = CreateValidQueueFile() with
        {
            Files =
            [
                CreateValidQueueFile().Files!.Single() with
                {
                    ServerUrl = serverUrl
                }
            ]
        };
        var json = QueueFileParser.Serialize(queueFile);

        var act = () => QueueFileParser.Parse(json);

        act.Should()
           .Throw<QueueFileValidationException>()
           .Which.Errors.Should().ContainSingle(error =>
                                                    error.Path == "files[0].serverUrl" &&
                                                    error.Message == "The serverUrl value must not include query string or fragment components.");
    }

    [Test]
    public void Parse_ShouldRejectTargetWithUserInfo()
    {
        var queueFile = CreateValidQueueFile() with
        {
            Files =
            [
                CreateValidQueueFile().Files!.Single() with
                {
                    ServerUrl = "https://user:pass@example.com/upload"
                }
            ]
        };
        var json = QueueFileParser.Serialize(queueFile);

        var act = () => QueueFileParser.Parse(json);

        act.Should()
           .Throw<QueueFileValidationException>()
           .Which.Errors.Should().ContainSingle(error =>
                                                    error.Path == "files[0].serverUrl" &&
                                                    error.Message == "The serverUrl value must not include user information.");
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
                    ServerUrl = "https://example.com",
                    ShareToken = "share-123",
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
    public void Serialize_ShouldRoundTripEmbeddedCredentials()
    {
        var queueFile = CreateValidQueueFile() with
        {
            Credentials = new QueueCredentials
            {
                ShareKey = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
                DownloadBearerToken = "bearer"
            }
        };

        var roundTripped = QueueFileParser.Deserialize(QueueFileParser.Serialize(queueFile));

        roundTripped.Credentials!.ShareKey.Should().Be("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef");
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
            .Equal("shadowDrop", "queueVersion", "files");

        var entry = root.GetProperty("files")[0];
        entry.EnumerateObject().Select(property => property.Name).Should()
             .Equal("serverUrl", "shareToken", "fileId", "fileName", "length", "outputPath", "plaintextSha256");
    }

    [Test]
    public void Validate_ShouldRejectCredentials_WhenShareKeyMalformed()
    {
        var queueFile = CreateValidQueueFile() with
        {
            Credentials = new QueueCredentials
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
            Credentials = new QueueCredentials
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
            Files = []
        };

        var errors = QueueFileParser.Validate(queueFile);

        errors.Should().BeEquivalentTo(
            [
                new("shadowDrop", "The shadowDrop value is required."),
                new("queueVersion", "The queueVersion value is required."),
                new QueueFileValidationError("files", "The files collection must contain at least one entry.")
            ],
            options => options.WithoutStrictOrdering());
    }

    private static QueueFile CreateValidQueueFile() =>
        new()
        {
            ShadowDrop = FormatConstants.ShadowDropVersion,
            QueueVersion = FormatConstants.QueueVersion,
            Files =
            [
                new()
                {
                    ServerUrl = "https://example.com",
                    ShareToken = "share-123",
                    FileId = "file-1",
                    FileName = "report.txt",
                    Length = 4096,
                    OutputPath = "downloads/report.txt",
                    PlaintextSha256 = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"
                }
            ]
        };
}
