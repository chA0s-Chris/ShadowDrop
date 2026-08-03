// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Infrastructure;

using Amazon.Runtime;
using Amazon.S3;
using Chaos.Mongo;
using Chaos.Mongo.Configuration;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using NUnit.Framework;
using ShadowDrop.Api;
using ShadowDrop.Api.Configuration;
using ShadowDrop.Api.Health;
using ShadowDrop.Api.Infrastructure.Mongo;
using ShadowDrop.Api.Uploads;
using ShadowDrop.Contracts;
using ShadowDrop.Tests.Uploads;
using System.Net;
using System.Text.Json;
using Testcontainers.MongoDb;

[TestFixture]
[Category("S3Integration")]
[NonParallelizable]
public sealed class RustFsS3IntegrationTests
{
    private const String AccessKey = "shadowdrop-test-access";
    private const String BucketName = "shadowdrop-test";
    private const String Region = "us-east-1";
    private const String SecretKey = "shadowdrop-test-secret-key";

    private AmazonS3Client _administrationClient;
    private IContainer _container;
    private String _endpoint;
    private IMongoHelper _mongo;
    private MongoDbContainer _mongoContainer;
    private ServiceProvider _mongoServices;
    private S3BlobStorage _storage;
    private AwsS3Client _storageClient;

    [OneTimeSetUp]
    public async Task StartDependenciesAsync()
    {
        _container = new ContainerBuilder()
                     .WithImage(RustFsTestImages.LatestBeta)
                     .WithEnvironment("RUSTFS_ACCESS_KEY", AccessKey)
                     .WithEnvironment("RUSTFS_SECRET_KEY", SecretKey)
                     .WithEnvironment("RUSTFS_CONSOLE_ENABLE", "false")
                     .WithPortBinding(9000, true)
                     .WithCommand("/data")
                     .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(request => request.ForPort(9000).ForPath("/health/ready")))
                     .Build();
        await _container.StartAsync();

        var endpoint = $"http://{_container.Hostname}:{_container.GetMappedPublicPort(9000)}";
        var s3Configuration = new AmazonS3Config
        {
            ServiceURL = endpoint,
            AuthenticationRegion = Region,
            ForcePathStyle = true
        };
        _administrationClient = new(new BasicAWSCredentials(AccessKey, SecretKey), s3Configuration);
        _ = await _administrationClient.PutBucketAsync(BucketName, CancellationToken.None);

        _endpoint = endpoint;
        var storageOptions = CreateStorageOptions();
        _storageClient = new(storageOptions);
        _storage = new(storageOptions, _storageClient, NullLogger<S3BlobStorage>.Instance);

        _mongoContainer = new MongoDbBuilder()
                          .WithImage(MongoDbTestImages.LatestStable)
                          .WithEnvironment("GLIBC_TUNABLES", "glibc.pthread.rseq=1")
                          .Build();
        await _mongoContainer.StartAsync();
        var mongoOptions = new ShadowDropOptions
        {
            Metadata = new()
            {
                Provider = MetadataProvider.MongoDb
            },
            Mongo = new()
            {
                ConnectionString = _mongoContainer.GetConnectionString(),
                DatabaseName = $"shadowdrop_s3_{Guid.NewGuid():N}"
            }
        };
        MongoSerialization.EnsureConfigured();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(mongoOptions);
        services.AddMongo(mongoOptions.Mongo.ConnectionString, mongoOptions.Mongo.DatabaseName, options =>
                {
                    options.UseDefaultCollectionNames = false;
                    options.AddMapping<MongoUploadedFileDocument>("uploaded_files");
                    options.AddMapping<MongoShareDocument>("shares");
                    options.AddMapping<MongoAdminTokenCredentialDocument>("admin_tokens");
                    options.AddMapping<MongoUploadCredentialDocument>("upload_credentials");
                })
                .WithConfigurator<ShadowDropMongoConfigurator>();
        services.AddSingleton<MongoUploadedFileMetadataRepository>();
        _mongoServices = services.BuildServiceProvider();
        _mongo = _mongoServices.GetRequiredService<IMongoHelper>();
        await _mongo.Database.RunCommandAsync<BsonDocument>(new BsonDocument("ping", 1));
        await _mongoServices.GetRequiredService<IMongoConfiguratorRunner>().RunConfiguratorsAsync();
    }

    [OneTimeTearDown]
    public async Task StopDependenciesAsync()
    {
        if (_mongoServices is not null)
        {
            await _mongo.Database.Client.DropDatabaseAsync(_mongo.Database.DatabaseNamespace.DatabaseName);
            await _mongoServices.DisposeAsync();
        }

        if (_mongoContainer is not null)
        {
            await _mongoContainer.DisposeAsync();
        }

        _administrationClient.Dispose();
        _storageClient.Dispose();
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    [Test]
    public async Task ProviderContract_ShouldPassAgainstRustFs()
    {
        await BlobStorageContract.AssertAsync(_storage);
    }

    [Test]
    public async Task MultipartUpload_ShouldRoundTripNonSeekableContent()
    {
        var content = new Byte[S3BlobStorage.MultipartPartSize + 1024];
        for (var index = 0; index < content.Length; index++)
        {
            content[index] = (Byte)(index % 251);
        }

        var fileId = Guid.NewGuid();
        using var source = new NonSeekableReadStream(content);

        var descriptor = await _storage.SaveAsync(fileId, source, CancellationToken.None);

        descriptor.BlobKey.Should().Be(fileId.ToString("N"));
        descriptor.WrittenLength.Should().Be(content.LongLength);
        await using (var downloaded = await _storage.OpenReadAsync(descriptor.BlobKey, CancellationToken.None))
        {
            var roundTripped = new Byte[content.Length];
            await downloaded.ReadExactlyAsync(roundTripped);
            roundTripped.Should().Equal(content);
            (await downloaded.ReadAsync(new Byte[1])).Should().Be(0);
        }

        (await _storage.DeleteIfExistsAsync(descriptor.BlobKey, CancellationToken.None)).Should().BeTrue();
    }

    [Test]
    public async Task ReadinessCheck_ShouldReportProvisionedBucketReady()
    {
        using var client = new AwsS3Client(CreateStorageOptions());
        var check = new S3OperationalDependencyProbe(client, CreateStorageOptions());

        var act = async () => await check.ProbeAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task FailedMultipartUpload_ShouldBeAborted(Boolean cancellation)
    {
        var content = new Byte[S3BlobStorage.MultipartPartSize + 1024];
        using var cancellationSource = new CancellationTokenSource();
        Exception failure = cancellation
            ? new OperationCanceledException()
            : new IOException("injected failure");
        Stream source = cancellation
            ? new CancelAfterStream(content, S3BlobStorage.MultipartPartSize + 1, cancellationSource)
            : new FailAfterStream(content, S3BlobStorage.MultipartPartSize + 1, failure);
        var save = async () => await _storage.SaveAsync(
            Guid.NewGuid(),
            source,
            // ReSharper disable once AccessToDisposedClosure
            cancellationSource.Token);

        await save.Should().ThrowAsync<Exception>().Where(exception => exception.GetType() == failure.GetType());
        var uploads = await _administrationClient.ListMultipartUploadsAsync(BucketName, CancellationToken.None);
        (uploads.MultipartUploads ?? []).Should().BeEmpty();
    }

    [TestCase(MetadataProvider.LiteDb)]
    [TestCase(MetadataProvider.MongoDb)]
    public async Task S3BlobStorage_ShouldCombineWithEitherMetadataProvider(MetadataProvider metadataProvider)
    {
        var root = Path.Combine(Path.GetTempPath(), $"shadowdrop-s3-matrix-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        IDisposable? disposable = null;
        try
        {
            IUploadedFileMetadataRepository repository;
            if (metadataProvider == MetadataProvider.LiteDb)
            {
                var liteDb = new LiteDbUploadedFileMetadataRepository(new()
                {
                    Metadata = new()
                    {
                        LiteDbPath = Path.Combine(root, "metadata", "shadowdrop.db")
                    }
                }, NullLogger<LiteDbUploadedFileMetadataRepository>.Instance);
                repository = liteDb;
                disposable = liteDb;
            }
            else
            {
                repository = _mongoServices.GetRequiredService<MongoUploadedFileMetadataRepository>();
            }

            var fileId = await repository.ReserveFileIdAsync(CancellationToken.None);
            (await repository.TryClaimReservationAsync(fileId, CancellationToken.None)).Should().BeTrue();
            var descriptor = await _storage.SaveAsync(fileId, new MemoryStream([1, 2, 3, 4]), CancellationToken.None);
            var record = new UploadedFileRecord(fileId, descriptor.BlobKey, "file.bin", 4, 4, "application/octet-stream",
                                                "v2", "aes", 1024, 1, "salt", null);

            (await repository.TryCompleteReservationAsync(record, CancellationToken.None)).Should().BeTrue();
            (await repository.GetAsync(fileId, CancellationToken.None))!.BlobKey.Should().Be(fileId.ToString("N"));
            (await _storage.DeleteIfExistsAsync(descriptor.BlobKey, CancellationToken.None)).Should().BeTrue();
        }
        finally
        {
            disposable?.Dispose();
            Directory.Delete(root, true);
        }
    }

    [TestCase(MetadataProvider.LiteDb)]
    [TestCase(MetadataProvider.MongoDb)]
    public async Task StatusEndpoint_ShouldBeReady_WithS3AndEitherMetadataProvider(MetadataProvider metadataProvider)
    {
        await using var factory = new S3StatusApiFactory(metadataProvider,
                                                         _endpoint,
                                                         _mongoContainer.GetConnectionString(),
                                                         _mongo.Database.DatabaseNamespace.DatabaseName);
        using var client = factory.CreateClient();

        using var readinessResponse = await client.GetAsync("/health/ready");
        using var statusResponse = await client.GetAsync("/api/status");

        readinessResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        statusResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var status = JsonDocument.Parse(await statusResponse.Content.ReadAsStringAsync());
        status.RootElement.GetProperty("ready").GetBoolean().Should().BeTrue();
        status.RootElement.GetProperty("reason").GetString().Should().Be(OperationalStatusReasons.None);
    }

    private ShadowDropOptions CreateStorageOptions() => new()
    {
        Storage = new()
        {
            Provider = BlobStorageProvider.S3,
            S3 = new()
            {
                BucketName = BucketName,
                Region = Region,
                ServiceEndpoint = _endpoint,
                UsePathStyle = true,
                AccessKeyId = AccessKey,
                SecretAccessKey = SecretKey
            }
        }
    };

    private sealed class S3StatusApiFactory : WebApplicationFactory<Program>
    {
        private readonly Dictionary<String, String?> _previousValues = [];
        private readonly String _rootDirectory;

        public S3StatusApiFactory(
            MetadataProvider metadataProvider,
            String endpoint,
            String mongoConnectionString,
            String mongoDatabaseName)
        {
            _rootDirectory = Path.Combine(Path.GetTempPath(), $"shadowdrop-s3-status-{Guid.NewGuid():N}");
            Directory.CreateDirectory(_rootDirectory);
            SetEnvironmentVariable("ShadowDrop__Metadata__Provider", metadataProvider.ToString());
            SetEnvironmentVariable("ShadowDrop__Metadata__LiteDbPath", Path.Combine(_rootDirectory, "metadata", "shadowdrop.db"));
            SetEnvironmentVariable("ShadowDrop__Storage__Provider", BlobStorageProvider.S3.ToString());
            SetEnvironmentVariable("ShadowDrop__Storage__LocalRoot", Path.Combine(_rootDirectory, "blobs"));
            SetEnvironmentVariable("ShadowDrop__Storage__S3__BucketName", BucketName);
            SetEnvironmentVariable("ShadowDrop__Storage__S3__Region", Region);
            SetEnvironmentVariable("ShadowDrop__Storage__S3__ServiceEndpoint", endpoint);
            SetEnvironmentVariable("ShadowDrop__Storage__S3__UsePathStyle", "true");
            SetEnvironmentVariable("ShadowDrop__Storage__S3__AccessKeyId", AccessKey);
            SetEnvironmentVariable("ShadowDrop__Storage__S3__SecretAccessKey", SecretKey);
            SetEnvironmentVariable("ShadowDrop__Mongo__ConnectionString", mongoConnectionString);
            SetEnvironmentVariable("ShadowDrop__Mongo__DatabaseName", mongoDatabaseName);
            SetEnvironmentVariable("ShadowDrop__ApiExposure__EnableAdminOperations", "false");
            SetEnvironmentVariable("ShadowDrop__ApiExposure__EnableUploads", "false");
            SetEnvironmentVariable("ShadowDrop__ApiExposure__EnablePublicDownloads", "true");
        }

        protected override void Dispose(Boolean disposing)
        {
            if (disposing)
            {
                foreach (var (key, value) in _previousValues)
                {
                    Environment.SetEnvironmentVariable(key, value);
                }
            }

            base.Dispose(disposing);

            if (Directory.Exists(_rootDirectory))
            {
                Directory.Delete(_rootDirectory, true);
            }
        }

        private void SetEnvironmentVariable(String key, String? value)
        {
            _previousValues[key] = Environment.GetEnvironmentVariable(key);
            Environment.SetEnvironmentVariable(key, value);
        }
    }

    private sealed class NonSeekableReadStream(Byte[] content) : MemoryStream(content, false)
    {
        public override Boolean CanSeek => false;

        public override Int64 Seek(Int64 offset, SeekOrigin loc) => throw new NotSupportedException();
    }

    private sealed class FailAfterStream(Byte[] content, Int32 throwAfter, Exception failure) : Stream
    {
        private Int32 _position;

        public override Boolean CanRead => true;
        public override Boolean CanSeek => false;
        public override Boolean CanWrite => false;
        public override Int64 Length => content.Length;
        public override Int64 Position { get => _position; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override Int32 Read(Byte[] buffer, Int32 offset, Int32 count) => throw new NotSupportedException();

        public override async ValueTask<Int32> ReadAsync(Memory<Byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            if (_position >= throwAfter)
            {
                throw failure;
            }

            var count = Math.Min(buffer.Length, Math.Min(throwAfter - _position, content.Length - _position));
            content.AsMemory(_position, count).CopyTo(buffer);
            _position += count;
            return count;
        }

        public override Int64 Seek(Int64 offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(Int64 value) => throw new NotSupportedException();
        public override void Write(Byte[] buffer, Int32 offset, Int32 count) => throw new NotSupportedException();
    }

    private sealed class CancelAfterStream(Byte[] content, Int32 cancelAfter, CancellationTokenSource cancellation) : Stream
    {
        private Int32 _position;

        public override Boolean CanRead => true;
        public override Boolean CanSeek => false;
        public override Boolean CanWrite => false;
        public override Int64 Length => content.Length;
        public override Int64 Position { get => _position; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override Int32 Read(Byte[] buffer, Int32 offset, Int32 count) => throw new NotSupportedException();

        public override async ValueTask<Int32> ReadAsync(Memory<Byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            if (_position >= cancelAfter)
            {
                await cancellation.CancelAsync();
                cancellationToken.ThrowIfCancellationRequested();
            }

            var count = Math.Min(buffer.Length, Math.Min(cancelAfter - _position, content.Length - _position));
            content.AsMemory(_position, count).CopyTo(buffer);
            _position += count;
            return count;
        }

        public override Int64 Seek(Int64 offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(Int64 value) => throw new NotSupportedException();
        public override void Write(Byte[] buffer, Int32 offset, Int32 count) => throw new NotSupportedException();
    }
}
