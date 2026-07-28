// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Configuration;

using FluentAssertions;
using Microsoft.Extensions.Configuration;
using NUnit.Framework;
using ShadowDrop.Api.Configuration;
using ShadowDrop.Api.Uploads;

[TestFixture]
public sealed class S3ConfigurationTests
{
    [TestCase("ShadowDrop:Storage:S3:BucketName", "")]
    [TestCase("ShadowDrop:Storage:S3:Region", "")]
    [TestCase("ShadowDrop:Storage:S3:ServiceEndpoint", "relative/path")]
    [TestCase("ShadowDrop:Storage:S3:ServiceEndpoint", "ftp://storage.example")]
    [TestCase("ShadowDrop:Storage:S3:AccessKeyId", "only-access-key")]
    [TestCase("ShadowDrop:Storage:S3:SecretAccessKey", "only-secret-key")]
    [TestCase("ShadowDrop:Storage:S3:KeyPrefix", "///")]
    public void BindAndValidate_ShouldRejectInvalidSelectedS3Settings(String key, String value)
    {
        var values = ValidValues();
        if (key.EndsWith("AccessKeyId", StringComparison.Ordinal))
        {
            values["ShadowDrop:Storage:S3:SecretAccessKey"] = String.Empty;
        }

        if (key.EndsWith("SecretAccessKey", StringComparison.Ordinal))
        {
            values["ShadowDrop:Storage:S3:AccessKeyId"] = String.Empty;
        }

        values[key] = value;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var bind = () => ShadowDropOptionsBinding.BindAndValidate(configuration, Directory.GetCurrentDirectory());

        bind.Should().Throw<InvalidOperationException>();
    }

    [Test]
    public void BindAndValidate_ShouldNormalizePrefixAndAcceptStaticCredentialPair()
    {
        var values = ValidValues();
        values["ShadowDrop:Storage:S3:KeyPrefix"] = "  tenant//archive/ ";
        values["ShadowDrop:Storage:S3:AccessKeyId"] = "access";
        values["ShadowDrop:Storage:S3:SecretAccessKey"] = "secret";
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        var options = ShadowDropOptionsBinding.BindAndValidate(configuration, Directory.GetCurrentDirectory());

        options.Storage.S3.KeyPrefix.Should().Be("tenant//archive");
    }

    [Test]
    public void BindAndValidate_ShouldRejectUploadLimitBeyondFixedMultipartCapacity()
    {
        var values = ValidValues();
        values["ShadowDrop:Upload:MaxBytes"] = (S3BlobStorage.MaximumObjectSize + 1).ToString();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var bind = () => ShadowDropOptionsBinding.BindAndValidate(configuration, Directory.GetCurrentDirectory());

        bind.Should().Throw<InvalidOperationException>().WithMessage("*Upload:MaxBytes*");
    }

    [Test]
    public void BindAndValidate_ShouldAcceptUploadLimitAtFixedMultipartCapacity()
    {
        var values = ValidValues();
        values["ShadowDrop:Upload:MaxBytes"] = S3BlobStorage.MaximumObjectSize.ToString();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        var bind = () => ShadowDropOptionsBinding.BindAndValidate(configuration, Directory.GetCurrentDirectory());

        bind.Should().NotThrow();
    }

    [Test]
    public void BindAndValidate_ShouldIgnoreS3Settings_WhenAnotherProviderIsSelected()
    {
        var values = ValidValues();
        values["ShadowDrop:Storage:Provider"] = nameof(BlobStorageProvider.MongoGridFs);
        values["ShadowDrop:Storage:S3:BucketName"] = String.Empty;
        values["ShadowDrop:Storage:S3:Region"] = String.Empty;
        values["ShadowDrop:Storage:S3:ServiceEndpoint"] = "not a URI";
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        var bind = () => ShadowDropOptionsBinding.BindAndValidate(configuration, Directory.GetCurrentDirectory());

        bind.Should().NotThrow();
    }

    private static Dictionary<String, String?> ValidValues() => new()
    {
        ["ShadowDrop:Metadata:Provider"] = nameof(MetadataProvider.MongoDb),
        ["ShadowDrop:Mongo:ConnectionString"] = "mongodb://localhost:27017",
        ["ShadowDrop:Mongo:DatabaseName"] = "shadowdrop-test",
        ["ShadowDrop:Storage:Provider"] = nameof(BlobStorageProvider.S3),
        ["ShadowDrop:Storage:GridFsBucketName"] = "shadowdrop-test",
        ["ShadowDrop:Storage:S3:BucketName"] = "shadowdrop-test",
        ["ShadowDrop:Storage:S3:Region"] = "us-east-1",
        ["ShadowDrop:Storage:S3:ServiceEndpoint"] = "https://s3.example.test",
        ["ShadowDrop:Storage:S3:UsePathStyle"] = "true",
        ["ShadowDrop:Cleanup:CronExpression"] = "0 * * * *",
        ["ShadowDrop:Upload:MaxBytes"] = (4L * 1024 * 1024 * 1024).ToString()
    };
}
