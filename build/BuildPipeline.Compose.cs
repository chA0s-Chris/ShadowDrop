// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.CI;

using Nuke.Common;
using Nuke.Common.IO;
using System.Text;
using System.Text.Json;

internal partial class BuildPipeline
{
    private Target SmokeTestDockerCompose => target =>
        target.DependsOn(Build, PublishApi, BuildDockerImage)
              .Description(
                  "Opt-in persistence smoke test for the local and MongoDB Compose deployments; requires Docker Compose and host port 19423.")
              .Executes(() =>
              {
                  SmokeTestComposeVariant("local", RootDirectory / "docker/compose.local.yaml", false);
                  SmokeTestComposeVariant("mongodb", RootDirectory / "docker/compose.mongodb.yaml", true);
              });

    private static void AssertMongoHealthGate(ProcessResult renderedConfiguration)
    {
        using var configuration = JsonDocument.Parse(renderedConfiguration.StandardOutput);
        var dependency = configuration.RootElement
                                      .GetProperty("services")
                                      .GetProperty("shadowdrop")
                                      .GetProperty("depends_on")
                                      .GetProperty("mongodb");
        (dependency.GetProperty("condition").GetString() ?? String.Empty).ShouldBe("service_healthy");
        dependency.GetProperty("required").GetBoolean().ShouldBeTrue();
    }

    private static void AssertMongoIsHealthy(String containerName)
    {
        var health = RunDocker(["inspect", "--format", "{{.State.Health.Status}}", containerName]);
        health.StandardOutput.Trim().ShouldBe("healthy");
    }

    private static String GetComposeContainerId(IReadOnlyCollection<String> prefix, String service)
    {
        var arguments = new List<String>(prefix);
        arguments.AddRange(["ps", "--quiet", service]);
        var result = RunDocker(arguments);
        var containerId = result.StandardOutput.Trim();
        return String.IsNullOrWhiteSpace(containerId)
            ? throw new InvalidOperationException($"Compose did not create the '{service}' service.")
            : containerId;
    }

    private static String RequireOutputValue(ProcessResult result, String prefix)
    {
        var value = result.StandardOutput
                          .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                          .FirstOrDefault(line => line.StartsWith(prefix, StringComparison.Ordinal));
        return value is null
            ? throw new InvalidOperationException($"CLI output did not contain '{prefix}'.")
            : value[prefix.Length..];
    }

    private void AssertComposeProviders(String containerName, Boolean mongoVariant)
    {
        var logs = RunDocker(["logs", containerName]);
        var expectedMetadata = mongoVariant ? "MetadataProvider: MongoDb" : "MetadataProvider: LiteDb";
        var expectedBlobs = mongoVariant ? "BlobProvider: MongoGridFs" : "BlobProvider: FileSystem";
        var output = logs.StandardOutput + logs.StandardError;

        Assert.True(output.Contains(expectedMetadata, StringComparison.Ordinal), $"Container logs did not contain '{expectedMetadata}'.");
        Assert.True(output.Contains(expectedBlobs, StringComparison.Ordinal), $"Container logs did not contain '{expectedBlobs}'.");
    }

    private void SmokeTestComposeVariant(String variant, AbsolutePath composeFile, Boolean mongoVariant)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var projectName = $"shadowdrop-smoke-{variant}-{suffix}";
        var workspace = ArtifactsDirectory / "compose-smoke" / projectName;
        var overrideFile = workspace / "compose.override.yaml";
        var environmentFile = workspace / ".env";
        var inputFile = workspace / "representative-data.bin";
        var downloadedFile = workspace / "downloaded-data.bin";
        var adminToken = $"compose-admin-{suffix}";
        var mongoUser = $"compose_user_{suffix}";
        var mongoPassword = $"compose_password_{suffix}";
        var cliAssembly = SourceDirectory / $"ShadowDrop.Cli/bin/{TargetBuildConfiguration}/net10.0/ShadowDrop.Cli.dll";
        var composePrefix = new List<String>
        {
            "compose",
            "--project-name",
            projectName,
            "--env-file",
            environmentFile,
            "--file",
            composeFile,
            "--file",
            overrideFile
        };

        workspace.CreateOrCleanDirectory();

        try
        {
            var overrideContents = $"services:{Environment.NewLine}" +
                                   $"  shadowdrop:{Environment.NewLine}" +
                                   $"    image: {GetDockerImageTag()}{Environment.NewLine}";
            if (mongoVariant)
            {
                // The current image disables glibc rseq registration, which MongoDB 8.3 needs on Linux kernel 6.19+.
                // Keep this host-specific test workaround out of the committed operator-facing Compose file.
                overrideContents += $"  mongodb:{Environment.NewLine}" +
                                    $"    environment:{Environment.NewLine}" +
                                    $"      GLIBC_TUNABLES: glibc.pthread.rseq=1{Environment.NewLine}";
            }

            File.WriteAllText(overrideFile, overrideContents);
            File.WriteAllText(environmentFile,
                              $"SHADOWDROP_BOOTSTRAP_ADMIN_TOKEN={adminToken}{Environment.NewLine}" +
                              $"MONGO_INITDB_ROOT_USERNAME={mongoUser}{Environment.NewLine}" +
                              $"MONGO_INITDB_ROOT_PASSWORD={mongoPassword}{Environment.NewLine}" +
                              $"SHADOWDROP_MONGO_CONNECTION_STRING=mongodb://{mongoUser}:{mongoPassword}@mongodb:27017/?authSource=admin{Environment.NewLine}" +
                              $"SHADOWDROP_BIND_ADDRESS=127.0.0.1{Environment.NewLine}");
            File.WriteAllBytes(inputFile, Encoding.UTF8.GetBytes($"ShadowDrop Compose persistence smoke test: {suffix}"));

            if (mongoVariant)
            {
                var configArguments = new List<String>(composePrefix);
                configArguments.AddRange(["config", "--format", "json"]);
                AssertMongoHealthGate(RunDocker(configArguments, logProcessOutput: false));
            }

            var upArguments = new List<String>(composePrefix);
            upArguments.AddRange(["up", "--detach"]);
            RunDocker(upArguments);

            var apiContainer = GetComposeContainerId(composePrefix, "shadowdrop");
            WaitForHealthyContainer(apiContainer, new("http://127.0.0.1:19423/health/ready"));
            AssertComposeProviders(apiContainer, mongoVariant);
            if (mongoVariant)
            {
                AssertMongoIsHealthy(GetComposeContainerId(composePrefix, "mongodb"));
            }

            var upload = RunProcess("dotnet",
                                    [cliAssembly, "upload", inputFile, "--server-url", "http://127.0.0.1:19423", "--upload-token", adminToken],
                                    workspace);
            var shareUrl = RequireOutputValue(upload, "share-url:");
            var shareKey = RequireOutputValue(upload, "share-key:");

            var recreateArguments = new List<String>(composePrefix);
            recreateArguments.AddRange(["up", "--detach", "--force-recreate"]);
            RunDocker(recreateArguments);

            apiContainer = GetComposeContainerId(composePrefix, "shadowdrop");
            WaitForHealthyContainer(apiContainer, new("http://127.0.0.1:19423/health/ready"));

            RunProcess("dotnet",
                       [cliAssembly, "download", shareUrl, "--share-key", shareKey, "--out", downloadedFile],
                       workspace);
            File.ReadAllBytes(downloadedFile).ShouldBe(File.ReadAllBytes(inputFile));

            // A second authenticated upload proves that the original bootstrap credential remains usable after recreation.
            RunProcess("dotnet",
                       [cliAssembly, "upload", inputFile, "--server-url", "http://127.0.0.1:19423", "--upload-token", adminToken],
                       workspace);
        }
        finally
        {
            var downArguments = new List<String>(composePrefix);
            downArguments.AddRange(["down", "--volumes", "--remove-orphans"]);
            RunDockerBestEffort(downArguments);
            workspace.DeleteDirectory();
        }
    }
}

internal static class ComposeSmokeAssertions
{
    public static void ShouldBe(this String actual, String expected) =>
        Assert.True(String.Equals(actual, expected, StringComparison.Ordinal), $"Expected '{expected}', but found '{actual}'.");

    public static void ShouldBe(this Byte[] actual, Byte[] expected) =>
        Assert.True(actual.SequenceEqual(expected), "Persisted plaintext differed after Compose service recreation.");

    public static void ShouldBeTrue(this Boolean actual) => Assert.True(actual, "Expected value to be true.");
}
