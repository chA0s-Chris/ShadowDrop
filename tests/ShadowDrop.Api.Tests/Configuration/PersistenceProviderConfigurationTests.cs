// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Configuration;

using Chaos.Mongo;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using NUnit.Framework;
using Serilog;
using ShadowDrop.Api.CompositionRoot;
using ShadowDrop.Api.Configuration;
using ShadowDrop.Api.Shares;
using ShadowDrop.Api.Uploads;

[TestFixture]
public sealed class PersistenceProviderConfigurationTests
{
    [Test]
    public void BindAndValidate_ShouldNotRequireOrInitializeUnusedLocalSettings_ForMongoOnlyProviders()
    {
        using var root = new TemporaryDirectory();
        var configuration = BuildConfiguration(MetadataProvider.MongoDb, BlobStorageProvider.MongoGridFs, root.Path,
                                               String.Empty, String.Empty);

        var options = ShadowDropOptionsBinding.BindAndValidate(configuration, root.Path);

        options.Metadata.LiteDbPath.Should().BeEmpty();
        options.Storage.LocalRoot.Should().BeEmpty();
        Directory.GetFileSystemEntries(root.Path).Should().BeEmpty();
    }

    [Test]
    public void BindAndValidate_ShouldRequireMongoSettings_WhenEitherProviderUsesMongoDb()
    {
        using var root = new TemporaryDirectory();
        var values = Values(MetadataProvider.LiteDb, BlobStorageProvider.MongoGridFs, root.Path);
        values["ShadowDrop:Mongo:ConnectionString"] = String.Empty;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        // ReSharper disable once AccessToDisposedClosure
        var act = () => ShadowDropOptionsBinding.BindAndValidate(configuration, root.Path);

        act.Should().Throw<InvalidOperationException>().WithMessage("*Mongo:ConnectionString*");
    }

    [TestCase(MetadataProvider.LiteDb, BlobStorageProvider.FileSystem,
              typeof(LiteDbUploadedFileMetadataRepository), typeof(LocalBlobStorage), false)]
    [TestCase(MetadataProvider.MongoDb, BlobStorageProvider.FileSystem,
              typeof(MongoUploadedFileMetadataRepository), typeof(LocalBlobStorage), true)]
    [TestCase(MetadataProvider.LiteDb, BlobStorageProvider.MongoGridFs,
              typeof(LiteDbUploadedFileMetadataRepository), typeof(MongoGridFsBlobStorage), true)]
    [TestCase(MetadataProvider.MongoDb, BlobStorageProvider.MongoGridFs,
              typeof(MongoUploadedFileMetadataRepository), typeof(MongoGridFsBlobStorage), true)]
    public void ConfigureServices_ShouldRegisterProvidersIndependently(MetadataProvider metadataProvider,
                                                                       BlobStorageProvider blobProvider,
                                                                       Type metadataImplementation,
                                                                       Type blobImplementation,
                                                                       Boolean expectsMongo)
    {
        using var root = new TemporaryDirectory();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ContentRootPath = root.Path
        });
        builder.Configuration.AddInMemoryCollection(Values(metadataProvider, blobProvider, root.Path));
        using var logger = new LoggerConfiguration().CreateLogger();

        builder.ConfigureServices(logger);

        builder.Services.Last(x => x.ServiceType == typeof(IUploadedFileMetadataRepository)).ImplementationType
               .Should().Be(metadataImplementation);
        builder.Services.Last(x => x.ServiceType == typeof(IBlobStorage)).ImplementationType
               .Should().Be(blobImplementation);
        builder.Services.Any(x => x.ServiceType == typeof(IMongoHelper)).Should().Be(expectsMongo);
        builder.Services.Last(x => x.ServiceType == typeof(IShareMetadataRepository)).ImplementationType
               .Should().Be(metadataProvider == MetadataProvider.LiteDb
                                ? typeof(LiteDbShareMetadataRepository)
                                : typeof(MongoShareMetadataRepository));
        builder.Services.Last(x => x.ServiceType == typeof(IShareCleanupCoordinator)).ImplementationType
               .Should().Be(expectsMongo
                                ? typeof(MongoShareCleanupCoordinator)
                                : typeof(InProcessShareCleanupCoordinator));
    }

    [Test]
    public async Task PrepareStartupAsync_ShouldFail_WhenSelectedMongoDbIsUnavailable()
    {
        using var root = new TemporaryDirectory();
        var values = Values(MetadataProvider.MongoDb, BlobStorageProvider.MongoGridFs, root.Path);
        values["ShadowDrop:Mongo:ConnectionString"] =
            "mongodb://127.0.0.1:1/?serverSelectionTimeoutMS=200&connectTimeoutMS=200";
        values["ShadowDrop:ApiExposure:EnablePublicDownloads"] = "false";
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ContentRootPath = root.Path
        });
        builder.Configuration.AddInMemoryCollection(values);
        await using var logger = new LoggerConfiguration().CreateLogger();
        await using var app = builder.ConfigureServices(logger).Build();

        // ReSharper disable AccessToDisposedClosure
        var act = async () => await app.PrepareStartupAsync(logger, CancellationToken.None);
        // ReSharper restore AccessToDisposedClosure

        await act.Should().ThrowAsync<Exception>();
    }

    private static IConfiguration BuildConfiguration(MetadataProvider metadataProvider,
                                                     BlobStorageProvider blobProvider,
                                                     String root,
                                                     String? liteDbPath = null,
                                                     String? localRoot = null)
    {
        var values = Values(metadataProvider, blobProvider, root);
        if (liteDbPath is not null)
        {
            values["ShadowDrop:Metadata:LiteDbPath"] = liteDbPath;
        }

        if (localRoot is not null)
        {
            values["ShadowDrop:Storage:LocalRoot"] = localRoot;
        }

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private static Dictionary<String, String?> Values(MetadataProvider metadataProvider,
                                                      BlobStorageProvider blobProvider,
                                                      String root)
    {
        return new()
        {
            ["ShadowDrop:Metadata:Provider"] = metadataProvider.ToString(),
            ["ShadowDrop:Metadata:LiteDbPath"] = Path.Combine(root, "metadata", "shadowdrop.db"),
            ["ShadowDrop:Storage:Provider"] = blobProvider.ToString(),
            ["ShadowDrop:Storage:LocalRoot"] = Path.Combine(root, "blobs"),
            ["ShadowDrop:Storage:GridFsBucketName"] = "shadowdrop_blobs",
            ["ShadowDrop:Mongo:ConnectionString"] = "mongodb://localhost:27017",
            ["ShadowDrop:Mongo:DatabaseName"] = "shadowdrop",
            ["ShadowDrop:ApiExposure:EnablePublicDownloads"] = "true",
            ["ShadowDrop:ApiExposure:EnableAdminOperations"] = "false"
        };
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"shadowdrop-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public String Path { get; }

        public void Dispose() => Directory.Delete(Path, true);
    }
}
