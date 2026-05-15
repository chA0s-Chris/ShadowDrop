// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests;

using FluentAssertions;
using LiteDB;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using ShadowDrop.Api;
using ShadowDrop.Api.Configuration;
using System.Net;

[TestFixture]
[NonParallelizable]
public sealed class ApiWalkingSkeletonTests
{
    [Test]
    public async Task HealthEndpoint_ShouldBeAvailableWithoutAuthentication()
    {
        await using var fixture = new TestApiFactory();
        using var client = fixture.CreateClient();

        var response = await client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Test]
    public async Task ManagementEndpoint_ShouldRequireValidAdminBearerToken()
    {
        await using var fixture = new TestApiFactory();
        using var client = fixture.CreateClient();

        var unauthenticatedResponse = await client.GetAsync("/api/admin/management/ping");
        client.DefaultRequestHeaders.Authorization = new("Bearer", fixture.BootstrapToken);
        var authenticatedResponse = await client.GetAsync("/api/admin/management/ping");

        unauthenticatedResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        authenticatedResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Test]
    public async Task Startup_ShouldCreateConfiguredMetadataAndStorageDirectories()
    {
        await using var fixture = new TestApiFactory();
        using var client = fixture.CreateClient();

        _ = await client.GetAsync("/health");

        Directory.Exists(Path.GetDirectoryName(fixture.MetadataDatabasePath)!).Should().BeTrue();
        Directory.Exists(fixture.LocalStorageRoot).Should().BeTrue();
    }

    [Test]
    public async Task BootstrapToken_ShouldBeStoredAsProtectedHash()
    {
        await using var fixture = new TestApiFactory();
        using var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", fixture.BootstrapToken);

        _ = await client.GetAsync("/api/admin/management/ping");

        using var database = new LiteDatabase(fixture.MetadataDatabasePath);
        var credentials = database.GetCollection<AdminTokenCredentialDocument>("admin_tokens");
        var credential = credentials.FindById(1);

        credential.Should().NotBeNull();
        credential.TokenHashBase64.Should().NotBe(fixture.BootstrapToken);
        credential.SaltBase64.Should().NotBeNullOrWhiteSpace();
        credential.Iterations.Should().BeGreaterThan(0);
    }

    [Test]
    public async Task ManagementEndpoint_ShouldNotBeMapped_WhenAdminOperationsExposureIsDisabled()
    {
        await using var fixture = new TestApiFactory(false);
        using var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", fixture.BootstrapToken);

        var response = await client.GetAsync("/api/admin/management/ping");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task ManagementEndpoint_ShouldReturn401_ForWrongBearerToken()
    {
        await using var fixture = new TestApiFactory();
        using var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", "definitely-not-the-right-token");

        var response = await client.GetAsync("/api/admin/management/ping");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task UploadRoute_ShouldRequireValidAdminBearerToken()
    {
        await using var fixture = new TestApiFactory();
        using var client = fixture.CreateClient();

        var noAuthResponse = await client.PostAsync("/api/admin/uploads/placeholder", null);
        client.DefaultRequestHeaders.Authorization = new("Bearer", "wrong-token");
        var wrongTokenResponse = await client.PostAsync("/api/admin/uploads/placeholder", null);
        client.DefaultRequestHeaders.Authorization = new("Bearer", fixture.BootstrapToken);
        var validTokenResponse = await client.PostAsync("/api/admin/uploads/placeholder", null);

        noAuthResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        wrongTokenResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        validTokenResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Test]
    public async Task PublicDownloadEndpoint_ShouldReturn404_WhenPublicDownloadsAreDisabled()
    {
        await using var fixture = new TestApiFactory(enablePublicDownloads: false);
        using var client = fixture.CreateClient();

        var response = await client.GetAsync($"/api/downloads/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    public void Startup_ShouldFail_WhenBootstrapAdminTokenIsMissingOnFirstBoot()
    {
        using var fixture = new TestApiFactory(withBootstrapToken: false);

        Action act = () => fixture.CreateClient();

        act.Should().Throw<InvalidOperationException>();
    }

    [Test]
    public async Task Config_RelativePaths_ShouldBeResolvedToAbsolutePathsUnderContentRoot()
    {
        await using var fixture = new TestApiFactory(useRelativePaths: true);
        using var client = fixture.CreateClient();

        _ = await client.GetAsync("/health");

        var options = fixture.Services.GetRequiredService<ShadowDropOptions>();
        var contentRoot = fixture.Services.GetRequiredService<IWebHostEnvironment>().ContentRootPath;

        Path.IsPathRooted(options.Metadata.LiteDbPath).Should().BeTrue("a relative metadata path must be resolved to an absolute path");
        Path.IsPathRooted(options.Storage.LocalRoot).Should().BeTrue("a relative storage path must be resolved to an absolute path");
        options.Metadata.LiteDbPath.Should().StartWith(contentRoot, "the resolved metadata path must be anchored to the content root");
        options.Storage.LocalRoot.Should().StartWith(contentRoot, "the resolved storage path must be anchored to the content root");
        Directory.Exists(Path.GetDirectoryName(options.Metadata.LiteDbPath)!).Should().BeTrue();
        Directory.Exists(options.Storage.LocalRoot).Should().BeTrue();
    }

    [Test]
    public async Task Startup_ShouldSucceed_WhenAdminOperationsAreDisabled_EvenWithoutBootstrapToken()
    {
        await using var fixture = new TestApiFactory(false, withBootstrapToken: false);
        using var client = fixture.CreateClient();

        var response = await client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK,
                                        "when admin operations are disabled, AdminTokenService is never initialized so no bootstrap token is required");
    }

    [Test]
    public async Task Startup_BootstrapFailure_ShouldLeaveEnvironmentClean_ForSubsequentStartup()
    {
        var failedFixture = new TestApiFactory(withBootstrapToken: false);
        Action act = () => failedFixture.CreateClient();
        act.Should().Throw<InvalidOperationException>();
        await failedFixture.DisposeAsync();

        await using var healthyFixture = new TestApiFactory();
        using var client = healthyFixture.CreateClient();
        var response = await client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK,
                                        "disposal of a failed factory must restore environment variables so subsequent factories can start");
    }

    private sealed class AdminTokenCredentialDocument
    {
        public Int32 Id { get; set; }

        public Int32 Iterations { get; set; }

        public String SaltBase64 { get; set; } = String.Empty;

        public String TokenHashBase64 { get; set; } = String.Empty;
    }

    private sealed class TestApiFactory : WebApplicationFactory<Program>
    {
        private const String AdminOperationsExposureEnvironmentVariable = "ShadowDrop__ApiExposure__EnableAdminOperations";
        private const String BootstrapTokenEnvironmentVariable = "SHADOWDROP_BOOTSTRAP_ADMIN_TOKEN";
        private const String MetadataPathEnvironmentVariable = "ShadowDrop__Metadata__LiteDbPath";
        private const String PublicDownloadsExposureEnvironmentVariable = "ShadowDrop__ApiExposure__EnablePublicDownloads";
        private const String StorageRootEnvironmentVariable = "ShadowDrop__Storage__LocalRoot";
        private readonly String? _previousAdminOperationsExposure;
        private readonly String? _previousBootstrapToken;
        private readonly String? _previousMetadataPath;
        private readonly String? _previousPublicDownloadsExposure;
        private readonly String? _previousStorageRoot;
        private readonly String? _relativePathPrefix;
        private readonly String _temporaryRootDirectory;
        private readonly Boolean _useRelativePaths;
        private String? _resolvedRelativeRoot;

        public TestApiFactory(Boolean enableAdminOperations = true, Boolean enablePublicDownloads = true, Boolean withBootstrapToken = true,
                              Boolean useRelativePaths = false)
        {
            _useRelativePaths = useRelativePaths;
            BootstrapToken = "test-bootstrap-token";
            EnableAdminOperations = enableAdminOperations;

            if (useRelativePaths)
            {
                _relativePathPrefix = $"reltest-{Guid.NewGuid():N}";
                _temporaryRootDirectory = String.Empty;
                MetadataDatabasePath = Path.Combine(_relativePathPrefix, "metadata", "shadowdrop.db");
                LocalStorageRoot = Path.Combine(_relativePathPrefix, "storage");
            }
            else
            {
                _temporaryRootDirectory = Path.Combine(Path.GetTempPath(), $"shadowdrop-api-tests-{Guid.NewGuid():N}");
                Directory.CreateDirectory(_temporaryRootDirectory);
                MetadataDatabasePath = Path.Combine(_temporaryRootDirectory, "metadata", "shadowdrop.db");
                LocalStorageRoot = Path.Combine(_temporaryRootDirectory, "storage");
            }

            _previousBootstrapToken = Environment.GetEnvironmentVariable(BootstrapTokenEnvironmentVariable);
            _previousMetadataPath = Environment.GetEnvironmentVariable(MetadataPathEnvironmentVariable);
            _previousStorageRoot = Environment.GetEnvironmentVariable(StorageRootEnvironmentVariable);
            _previousPublicDownloadsExposure = Environment.GetEnvironmentVariable(PublicDownloadsExposureEnvironmentVariable);
            _previousAdminOperationsExposure = Environment.GetEnvironmentVariable(AdminOperationsExposureEnvironmentVariable);

            Environment.SetEnvironmentVariable(BootstrapTokenEnvironmentVariable, withBootstrapToken ? BootstrapToken : null);
            Environment.SetEnvironmentVariable(MetadataPathEnvironmentVariable, MetadataDatabasePath);
            Environment.SetEnvironmentVariable(StorageRootEnvironmentVariable, LocalStorageRoot);
            Environment.SetEnvironmentVariable(PublicDownloadsExposureEnvironmentVariable, enablePublicDownloads ? "true" : "false");
            Environment.SetEnvironmentVariable(AdminOperationsExposureEnvironmentVariable, enableAdminOperations ? "true" : "false");
        }

        public String BootstrapToken { get; }

        public Boolean EnableAdminOperations { get; }

        public String LocalStorageRoot { get; }

        public String MetadataDatabasePath { get; }

        protected override void Dispose(Boolean disposing)
        {
            if (disposing)
            {
                if (_useRelativePaths && _relativePathPrefix is not null)
                {
                    try
                    {
                        var contentRoot = Services.GetRequiredService<IWebHostEnvironment>().ContentRootPath;
                        _resolvedRelativeRoot = Path.Combine(contentRoot, _relativePathPrefix);
                    }
                    catch
                    {
                        // best-effort: Services may be inaccessible if startup failed
                    }
                }

                Environment.SetEnvironmentVariable(BootstrapTokenEnvironmentVariable, _previousBootstrapToken);
                Environment.SetEnvironmentVariable(MetadataPathEnvironmentVariable, _previousMetadataPath);
                Environment.SetEnvironmentVariable(StorageRootEnvironmentVariable, _previousStorageRoot);
                Environment.SetEnvironmentVariable(PublicDownloadsExposureEnvironmentVariable, _previousPublicDownloadsExposure);
                Environment.SetEnvironmentVariable(AdminOperationsExposureEnvironmentVariable, _previousAdminOperationsExposure);
            }

            base.Dispose(disposing);

            if (!String.IsNullOrEmpty(_temporaryRootDirectory) && Directory.Exists(_temporaryRootDirectory))
            {
                Directory.Delete(_temporaryRootDirectory, true);
            }

            if (_resolvedRelativeRoot is not null && Directory.Exists(_resolvedRelativeRoot))
            {
                Directory.Delete(_resolvedRelativeRoot, true);
            }
        }
    }
}
