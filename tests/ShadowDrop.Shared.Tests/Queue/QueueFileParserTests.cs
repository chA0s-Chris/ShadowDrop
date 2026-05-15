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
    public void Parse_ShouldRejectInvalidQueueFile()
    {
        const String json = """
                            {
                              "shadowDrop": "2.0",
                              "queueVersion": "1.0",
                              "target": "notaurl",
                              "shareId": "",
                              "files": [
                                {
                                  "fileId": "",
                                  "fileName": "",
                                  "length": -1,
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
                   new("shareId", "The shareId value is required."),
                   new("target", "The target value must be an absolute HTTP or HTTPS URL."),
                   new("shadowDrop", "The shadowDrop value must be '1.0'."),
                   new("files[0].fileId", "The fileId value is required."),
                   new("files[0].fileName", "The fileName value is required."),
                   new("files[0].length", "The file length must be zero or greater."),
                   new QueueFileValidationError("files[0].plaintextSha256",
                                                "The plaintextSha256 value must be a 64-character lowercase hexadecimal SHA-256 digest.")
               ],
               options => options.WithoutStrictOrdering());
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
                    Length = 4096
                }
            ]
        };

        var json = QueueFileParser.Serialize(queueFile);
        using var document = JsonDocument.Parse(json);
        var entry = document.RootElement.GetProperty("files")[0];

        entry.TryGetProperty("plaintextSha256", out _).Should().BeFalse();
    }

    [Test]
    public void Serialize_ShouldUseExactQueuePropertyNames()
    {
        var queueFile = CreateValidQueueFile();

        var json = QueueFileParser.Serialize(queueFile);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        root.EnumerateObject().Select(property => property.Name).Should()
            .Equal("shadowDrop", "queueVersion", "target", "shareId", "files");

        var entry = root.GetProperty("files")[0];
        entry.EnumerateObject().Select(property => property.Name).Should()
             .Equal("fileId", "fileName", "length", "plaintextSha256");
    }

    [Test]
    public void Validate_ShouldRejectMissingRequiredFieldsAndEmptyFiles()
    {
        var queueFile = new QueueFile
        {
            ShadowDrop = null,
            QueueVersion = null,
            Target = null,
            ShareId = null,
            Files = []
        };

        var errors = QueueFileParser.Validate(queueFile);

        errors.Should().BeEquivalentTo(
            [
                new("shadowDrop", "The shadowDrop value is required."),
                new("queueVersion", "The queueVersion value is required."),
                new("shareId", "The shareId value is required."),
                new("target", "The target value is required."),
                new QueueFileValidationError("files", "The files collection must contain at least one entry.")
            ],
            options => options.WithoutStrictOrdering());
    }

    private static QueueFile CreateValidQueueFile() =>
        new()
        {
            ShadowDrop = FormatConstants.ShadowDropVersion,
            QueueVersion = FormatConstants.QueueVersion,
            Target = "https://example.com",
            ShareId = "share-123",
            Files =
            [
                new()
                {
                    FileId = "file-1",
                    FileName = "report.txt",
                    Length = 4096,
                    PlaintextSha256 = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"
                }
            ]
        };
}
