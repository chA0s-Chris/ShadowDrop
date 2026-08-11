// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.CI;

using Nuke.Common;
using Nuke.Common.IO;
using Nuke.Common.Tools.DotNet;
using Serilog;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using static Nuke.Common.Tools.DotNet.DotNetTasks;

internal partial class BuildPipeline
{
    // `docker buildx build --platform` exposes TARGETARCH as amd64/arm64, while the CLI release
    // artifacts are named after their .NET runtime identifier. Resolving the mapping here keeps it out
    // of Dockerfile.cli, which cannot vary a build argument per platform in a single buildx invocation.
    private static readonly (String DockerArchitecture, String RuntimeIdentifier)[] CliDockerArchitectures =
    [
        ("amd64", "linux-x64"),
        ("arm64", "linux-arm64")
    ];

    private const String CliDockerImageRepository = "shadowdrop-cli";

    // The documented command users invoke; the versioned release artifact is derived from it. Independent
    // of the CLI assembly identity, which the publish output is named after.
    private const String CliExecutableName = "shadowdrop";

    // The name `dotnet publish` emits, i.e. the CLI assembly identity (the project default, since
    // ShadowDrop.Cli.csproj sets no <AssemblyName>). Copied to the versioned release artifact name
    // (see GetCliArtifactName) during publish.
    private const String CliPublishedAssemblyName = "ShadowDrop.Cli";
    private const String DockerContainerPort = "19423";

    // Serilog's default console template renders the level as `[{Timestamp:HH:mm:ss} {Level:u3}]`,
    // so Error/Fatal entries surface as ` ERR]`/` FTL]` level tokens. Match on those rather than a
    // free-text "Error"/"Fatal" substring, which both misses level-only signals and false-positives
    // on benign messages that merely contain those words.
    private static readonly String[] DockerErrorLogLevelTokens = [" ERR]", " FTL]"];
    private const String DockerImageRepository = "shadowdrop";
    private static readonly TimeSpan DockerSmokeTestTimeout = TimeSpan.FromSeconds(60);

    private static readonly String[] LinuxCliRuntimeIdentifiers =
    [
        "linux-x64",
        "linux-arm64"
    ];

    private static readonly String[] LinuxMuslArm64CliRuntimeIdentifiers =
    [
        "linux-musl-arm64"
    ];

    private static readonly String[] LinuxMuslX64CliRuntimeIdentifiers =
    [
        "linux-musl-x64"
    ];

    private static readonly String[] MacOsCliRuntimeIdentifiers =
    [
        "osx-x64",
        "osx-arm64"
    ];

    // Both images publish one tag backed by a manifest list covering these platforms. The API image
    // copies identical architecture-neutral IL onto each platform's runtime base; the CLI image instead
    // selects a per-architecture native binary staged by StageCliDockerArtifacts.
    private static readonly String[] MultiPlatformDockerPlatforms =
    [
        "linux/amd64",
        "linux/arm64"
    ];

    private static readonly String[] WindowsCliRuntimeIdentifiers =
    [
        "win-x64",
        "win-arm64"
    ];

    public Target Publish => target =>
        target.DependsOn(PublishApi, PublishCli);

    // Builds the CLI image for linux/amd64 + linux/arm64 as one manifest-list tag and loads it into the
    // local image store, mirroring BuildDockerImageMultiPlatform's requirements: Docker's containerd
    // image store plus QEMU/binfmt for the non-native architecture. Ordered After(PublishCliLinux) but
    // without depending on it, so existing release artifacts (e.g. restored from the release bundle in
    // CI) are reused instead of triggering a cross-compiling republish.
    private Target BuildCliDockerImageMultiPlatform => target =>
        target.DependsOn(StageCliDockerArtifacts)
              .After(PublishCliLinux)
              .Executes(() =>
              {
                  BuildDockerImageCore(RootDirectory / "Dockerfile.cli",
                                       GetCliDockerImageTag(),
                                       MultiPlatformDockerPlatforms,
                                       true);
              });

    private Target BuildDockerImage => target =>
        target.DependsOn(EnsurePublishApiArtifacts)
              .After(PublishApi)
              .Executes(() =>
              {
                  BuildDockerImageCore([], true);
              });

    // Builds a single multi-platform image for linux/amd64 + linux/arm64 (one tag backed by a manifest
    // list) and loads it into the local image store via `docker buildx build --platform ... --load`,
    // without pushing to any registry. Loading a multi-platform image requires the Docker containerd
    // image store plus QEMU/binfmt for the non-native architecture; this target does not configure the
    // daemon itself but surfaces a clear, actionable error (see BuildDockerImageCore) when the legacy
    // image store cannot satisfy the build. Ordered After(PublishApi) without DependsOn(PublishApi) so
    // it never forces a republish as part of the chain: existing API publish output (e.g. restored from
    // artifacts in CI) is reused as-is, and EnsurePublishApiArtifacts only publishes as a local-dev
    // fallback when the artifacts are missing.
    private Target BuildDockerImageMultiPlatform => target =>
        target.DependsOn(EnsurePublishApiArtifacts)
              .After(PublishApi)
              .Executes(() =>
              {
                  BuildDockerImageCore(MultiPlatformDockerPlatforms, true);
              });

    private Target EnsurePublishApiArtifacts => target =>
        target.After(Clean, RestoreTools, PublishApi)
              .Executes(() =>
              {
                  if (HasPublishApiArtifacts())
                  {
                      Log.Information("Using existing API publish output from {PublishApiDirectory}.", PublishApiDirectory);
                      return;
                  }

                  Log.Information("API publish output is missing. Publishing API before building the Docker image...");
                  PublishApiArtifacts(false);
              });

    private Target PublishApi => target =>
        target.DependsOn(Restore)
              .After(Clean, RestoreTools)
              .Executes(() =>
              {
                  PublishApiArtifacts(true);
              });

    private Target PublishCli => target =>
        target.DependsOn(Restore)
              .After(Clean, RestoreTools)
              .Executes(() =>
              {
                  Log.Information("Publishing CLI for the current platform...");

                  if (OperatingSystem.IsLinux())
                  {
                      PublishCliArtifacts(LinuxCliRuntimeIdentifiers);
                      return;
                  }

                  if (OperatingSystem.IsMacOS())
                  {
                      PublishCliArtifacts(MacOsCliRuntimeIdentifiers);
                      return;
                  }

                  if (OperatingSystem.IsWindows())
                  {
                      PublishCliArtifacts(WindowsCliRuntimeIdentifiers);
                      return;
                  }

                  throw new PlatformNotSupportedException("CLI publishing is supported on Linux, macOS, and Windows.");
              });

    private Target PublishCliLinux => target =>
        target.DependsOn(Restore)
              .After(Clean, RestoreTools)
              .Executes(() =>
              {
                  Log.Information("Publishing Linux CLI artifacts...");

                  PublishCliArtifacts(LinuxCliRuntimeIdentifiers);
              });

    private Target PublishCliLinuxMuslArm64 => target =>
        target.DependsOn(Restore)
              .After(Clean, RestoreTools)
              .Executes(() =>
              {
                  Log.Information("Publishing Linux musl arm64 CLI artifact...");

                  PublishCliArtifacts(LinuxMuslArm64CliRuntimeIdentifiers);
              });

    private Target PublishCliLinuxMuslX64 => target =>
        target.DependsOn(Restore)
              .After(Clean, RestoreTools)
              .Executes(() =>
              {
                  Log.Information("Publishing Linux musl x64 CLI artifact...");

                  PublishCliArtifacts(LinuxMuslX64CliRuntimeIdentifiers);
              });

    private Target PublishCliMacOs => target =>
        target.DependsOn(Restore)
              .After(Clean, RestoreTools)
              .Executes(() =>
              {
                  Log.Information("Publishing macOS CLI artifacts...");

                  PublishCliArtifacts(MacOsCliRuntimeIdentifiers);
              });

    private Target PublishCliWindows => target =>
        target.DependsOn(Restore)
              .After(Clean, RestoreTools)
              .Executes(() =>
              {
                  Log.Information("Publishing Windows CLI artifacts...");

                  PublishCliArtifacts(WindowsCliRuntimeIdentifiers);
              });

    // Runs the loaded manifest-list image once per platform, the non-native architecture under QEMU.
    // Asserting the exact version string rather than only the exit code catches a stale artifact bundle
    // as well as a wrong-architecture binary, which is the likeliest failure of the TARGETARCH wiring.
    private Target SmokeTestCliDockerImageMultiPlatform => target =>
        target.DependsOn(BuildCliDockerImageMultiPlatform)
              .Executes(() =>
              {
                  foreach (var platform in MultiPlatformDockerPlatforms)
                  {
                      Log.Information("Smoke testing multi-platform CLI image for platform {Platform}...", platform);
                      SmokeTestCliDockerImageCore(platform);
                  }
              });

    private Target SmokeTestDockerImage => target =>
        target.DependsOn(BuildDockerImage)
              .Executes(() => SmokeTestDockerImageCore());

    // Validates the loaded manifest-list image's runtime behavior on both amd64 and arm64 by running it
    // once per platform via `docker run --platform`. The non-native architecture runs under QEMU, which
    // is cheap here because the image only carries prebuilt IL. Each run is torn down in a finally-style
    // cleanup, and the build fails if either platform never becomes healthy.
    private Target SmokeTestDockerImageMultiPlatform => target =>
        target.DependsOn(BuildDockerImageMultiPlatform)
              .Executes(() =>
              {
                  foreach (var platform in MultiPlatformDockerPlatforms)
                  {
                      Log.Information("Smoke testing multi-platform image for platform {Platform}...", platform);
                      SmokeTestDockerImageCore(platform);
                  }
              });

    // Unlike EnsurePublishApiArtifacts, this never republishes as a local-dev fallback: producing the
    // linux-arm64 artifact needs a cross-linker (gcc-aarch64-linux-gnu) that a typical machine lacks, so
    // a silent republish would fail far less clearly than naming the target that produces the artifacts.
    private Target StageCliDockerArtifacts => target =>
        target.After(Clean, RestoreTools, PublishCliLinux)
              .Executes(StageCliDockerArtifactsCore);

    private static void AssertContainerLogsDoNotContainStartupErrors(String containerName)
    {
        var logs = RunDocker(["logs", containerName]);
        var startupLogs = logs.StandardOutput + "\n" + logs.StandardError;

        var offendingLines = startupLogs
                             .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                             .Where(line => DockerErrorLogLevelTokens.Any(token => line.Contains(token, StringComparison.Ordinal)))
                             .ToList();

        if (offendingLines.Count > 0)
        {
            Assert.Fail(
                $"Container '{containerName}' startup logs contain Error/Fatal entries:{Environment.NewLine}{String.Join(Environment.NewLine, offendingLines)}");
        }
    }

    private static void EnsureExecutableMode(AbsolutePath path, String runtimeIdentifier)
    {
        if (OperatingSystem.IsWindows() || IsWindowsRuntime(runtimeIdentifier))
            return;

        File.SetUnixFileMode(
            path,
            UnixFileMode.UserRead |
            UnixFileMode.UserWrite |
            UnixFileMode.UserExecute |
            UnixFileMode.GroupRead |
            UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead |
            UnixFileMode.OtherExecute);
    }

    private static String GetCliPublishedExecutableName(String runtimeIdentifier)
    {
        var extension = IsWindowsRuntime(runtimeIdentifier) ? ".exe" : String.Empty;
        return $"{CliPublishedAssemblyName}{extension}";
    }

    private static Int32 GetContainerHostPort(String containerName)
    {
        var portResult = RunDocker(["port", containerName, $"{DockerContainerPort}/tcp"]);
        var mappings = portResult.StandardOutput.Split(Environment.NewLine,
                                                       StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var mapping in mappings)
        {
            if (mapping.StartsWith("127.0.0.1:", StringComparison.Ordinal))
                return Int32.Parse(mapping["127.0.0.1:".Length..]);
        }

        foreach (var mapping in mappings)
        {
            var portSeparatorIndex = mapping.LastIndexOf(':');
            if (portSeparatorIndex >= 0 && Int32.TryParse(mapping[(portSeparatorIndex + 1)..], out var port))
                return port;
        }

        throw new InvalidOperationException(
            $"Docker did not report a host port mapping for container '{containerName}' port {DockerContainerPort}/tcp.");
    }

    private static String? GetCurrentRuntimeIdentifier()
    {
        var platform = OperatingSystem.IsLinux()
            ? RuntimeInformation.RuntimeIdentifier.StartsWith("linux-musl-", StringComparison.Ordinal)
                ? "linux-musl"
                : "linux"
            : OperatingSystem.IsMacOS()
                ? "osx"
                : OperatingSystem.IsWindows()
                    ? "win"
                    : null;
        var architecture = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            _ => null
        };
        return platform is null || architecture is null ? null : $"{platform}-{architecture}";
    }

    // buildx reports one of these signatures when asked to build/load a multi-platform image on the legacy
    // `docker` driver (image store) instead of the containerd image store.
    private static Boolean IndicatesMissingContainerdImageStore(String output) =>
        output.Contains("Multi-platform build is not supported for the docker driver", StringComparison.OrdinalIgnoreCase) ||
        output.Contains("containerd image store", StringComparison.OrdinalIgnoreCase) ||
        output.Contains("docker exporter does not currently support exporting manifest lists", StringComparison.OrdinalIgnoreCase);

    private static Boolean IsContainerRunning(String containerName)
    {
        var result = RunDocker(["inspect", "--format", "{{.State.Running}}", containerName], true);
        return result.ExitCode == 0 && String.Equals(result.StandardOutput.Trim(), "true", StringComparison.OrdinalIgnoreCase);
    }

    private static Boolean IsWindowsRuntime(String runtimeIdentifier) =>
        runtimeIdentifier.StartsWith("win-", StringComparison.Ordinal);

    private static ProcessResult RunDocker(IReadOnlyCollection<String> arguments,
                                           Boolean ignoreExitCode = false,
                                           Boolean logProcessOutput = true) =>
        RunProcess("docker", arguments, RootDirectory, ignoreExitCode, logProcessOutput);

    private static void RunDockerBestEffort(IReadOnlyCollection<String> arguments)
    {
        try
        {
            RunDocker(arguments, true);
        }
        catch (Exception e)
        {
            Log.Warning(e, "Best-effort Docker cleanup failed.");
        }
    }

    private static ProcessResult RunProcess(String fileName,
                                            IReadOnlyCollection<String> arguments,
                                            AbsolutePath workingDirectory,
                                            Boolean ignoreExitCode = false,
                                            Boolean logProcessOutput = true)
    {
        var standardOutput = new StringBuilder();
        var standardError = new StringBuilder();

        using var process = new Process();
        process.StartInfo = new()
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory.ToString(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        foreach (var argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null)
                return;

            standardOutput.AppendLine(e.Data);
            if (logProcessOutput)
                Log.Information("{ProcessOutput}", e.Data);
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null)
                return;

            standardError.AppendLine(e.Data);
            if (logProcessOutput)
                Log.Information("{ProcessOutput}", e.Data);
        };

        var command = $"{fileName} {String.Join(" ", arguments)}";
        Log.Information("Running {Command}", command);

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        process.WaitForExit();

        var result = new ProcessResult(process.ExitCode, standardOutput.ToString(), standardError.ToString());
        if (result.ExitCode != 0 && !ignoreExitCode)
        {
            Assert.Fail(
                $"Command failed with exit code {result.ExitCode}: {command}{Environment.NewLine}{result.StandardOutput}{result.StandardError}");
        }

        return result;
    }

    private static void SmokeTestPublishedCliStatus(AbsolutePath executable)
    {
        const String responseBody =
            "{\"protocolVersion\":1,\"live\":true,\"ready\":true,\"reason\":\"none\",\"capabilities\":{\"publicDownloads\":true,\"adminOperations\":true,\"resumableDownloads\":true,\"scopedUploads\":true}}";
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        listener.Start();
        var endpoint = (IPEndPoint)listener.LocalEndpoint;
        var responseTask = Task.Run(async () =>
        {
            using var connection = await listener.AcceptTcpClientAsync(timeout.Token);
            await using var stream = connection.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, false, leaveOpen: true);
            while (!String.IsNullOrEmpty(await reader.ReadLineAsync(timeout.Token))) { }

            var body = Encoding.UTF8.GetBytes(responseBody);
            var headers = Encoding.ASCII.GetBytes(
                $"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(headers, timeout.Token);
            await stream.WriteAsync(body, timeout.Token);
        }, timeout.Token);

        ProcessResult result;
        try
        {
            result = RunProcess(executable.ToString(),
                                ["server", "status", "--server-url", $"http://127.0.0.1:{endpoint.Port}", "--json", "--no-banner"],
                                RootDirectory,
                                true,
                                false);
            responseTask.GetAwaiter().GetResult();
        }
        finally
        {
            timeout.Cancel();
            listener.Stop();
        }

        Assert.True(result.ExitCode == 0,
                    $"Published CLI status smoke test failed:{Environment.NewLine}{result.StandardError}");
        Assert.True(String.IsNullOrWhiteSpace(result.StandardError),
                    $"Published CLI status smoke test wrote to stderr:{Environment.NewLine}{result.StandardError}");
        var outputLines = result.StandardOutput.Split(Environment.NewLine,
                                                      StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.True(outputLines.Length == 1, "Published CLI status smoke test must emit exactly one JSON result.");
        using var document = JsonDocument.Parse(outputLines[0]);
        Assert.True(String.Equals("healthy", document.RootElement.GetProperty("outcome").GetString(), StringComparison.Ordinal),
                    "Published CLI status smoke test must report a healthy outcome.");
        Assert.True(document.RootElement.GetProperty("serverStatus").GetProperty("protocolVersion").GetInt32() == 1,
                    "Published CLI status smoke test must deserialize protocol version 1.");
    }

    private static void WaitForHealthyContainer(String containerName, Uri healthEndpoint)
    {
        using var httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(2)
        };
        var stopwatch = Stopwatch.StartNew();
        Exception? lastException = null;

        while (stopwatch.Elapsed < DockerSmokeTestTimeout)
        {
            if (!IsContainerRunning(containerName))
                Assert.Fail($"Container '{containerName}' exited before the smoke test observed a healthy response.");

            AssertContainerLogsDoNotContainStartupErrors(containerName);

            try
            {
                using var response = httpClient.GetAsync(healthEndpoint).GetAwaiter().GetResult();
                if (response.StatusCode == HttpStatusCode.OK)
                    return;
            }
            catch (Exception e)
            {
                lastException = e;
            }

            Thread.Sleep(TimeSpan.FromSeconds(1));
        }

        var message = $"Container '{containerName}' did not return HTTP 200 from {healthEndpoint} within {DockerSmokeTestTimeout}.";
        if (lastException is not null)
            message += $"{Environment.NewLine}Last request failure: {lastException.Message}";

        Assert.Fail(message);
    }

    private void BuildDockerImageCore(IReadOnlyCollection<String> platforms, Boolean loadIntoLocalStore)
    {
        EnsurePublishApiArtifactsExist();

        BuildDockerImageCore(RootDirectory / "Dockerfile", GetDockerImageTag(), platforms, loadIntoLocalStore);
    }

    private void BuildDockerImageCore(AbsolutePath dockerfile,
                                      String imageTag,
                                      IReadOnlyCollection<String> platforms,
                                      Boolean loadIntoLocalStore)
    {
        var arguments = new List<String>
        {
            "buildx",
            "build",
            "--file",
            dockerfile.ToString(),
            "--tag",
            imageTag
        };

        if (platforms.Count > 0)
        {
            arguments.Add("--platform");
            arguments.Add(String.Join(",", platforms));
        }

        if (loadIntoLocalStore)
            arguments.Add("--load");

        arguments.Add(RootDirectory.ToString());

        var result = RunDocker(arguments, true);
        if (result.ExitCode == 0)
            return;

        var output = $"{result.StandardOutput}{Environment.NewLine}{result.StandardError}";

        if (platforms.Count > 1 && IndicatesMissingContainerdImageStore(output))
        {
            Assert.Fail(
                "Building and loading a multi-platform image requires Docker's containerd image store and " +
                "QEMU/binfmt for the non-native architecture, but the Docker daemon is using the legacy image " +
                "store, which cannot build or load multi-platform images. Enable the containerd image store " +
                "(Docker Desktop: Settings > General > 'Use containerd for pulling and storing images'; Docker " +
                "Engine: set { \"features\": { \"containerd-snapshotter\": true } } in the daemon configuration " +
                "and restart the daemon), ensure QEMU/binfmt is installed (e.g. docker/setup-qemu-action in CI), " +
                $"then retry.{Environment.NewLine}{output}");
        }

        Assert.Fail($"docker buildx build failed with exit code {result.ExitCode}.{Environment.NewLine}{output}");
    }

    private void EnsurePublishApiArtifactsExist()
    {
        if (!HasPublishApiArtifacts())
        {
            Assert.Fail(
                $"API publish output is missing or empty at '{PublishApiDirectory}'. Run the EnsurePublishApiArtifacts target before invoking Docker image helpers directly.");
        }
    }

    private String GetCliArtifactName(String runtimeIdentifier)
    {
        var extension = IsWindowsRuntime(runtimeIdentifier) ? ".exe" : String.Empty;
        return $"{CliExecutableName}-{SemanticVersion}-{runtimeIdentifier}{extension}";
    }

    // Matches the source_image that scripts/calculate-docker-tags.sh derives from the Docker Hub
    // repository's final path segment, so the release workflow can retag exactly what was built.
    private String GetCliDockerImageTag() => $"{CliDockerImageRepository}:{SemanticVersion}";

    private String GetDockerImageTag() => $"{DockerImageRepository}:{SemanticVersion}";

    private Boolean HasPublishApiArtifacts() =>
        (PublishApiDirectory / "ShadowDrop.Api.dll").FileExists() &&
        (PublishHealthProbeDirectory / "ShadowDrop.HealthProbe.dll").FileExists();

    private void PublishApiArtifacts(Boolean noRestore)
    {
        Log.Information("Publishing API...");

        DotNetPublish(s =>
        {
            s = s.SetProject(ProjectFileApi)
                 .SetConfiguration(TargetBuildConfiguration)
                 .SetOutput(PublishApiDirectory)
                 .EnableContinuousIntegrationBuild();

            if (noRestore)
                s = s.EnableNoRestore();

            return s;
        });

        Log.Information("Publishing API health probe...");
        DotNetPublish(s =>
        {
            s = s.SetProject(ProjectFileHealthProbe)
                 .SetConfiguration(TargetBuildConfiguration)
                 .SetOutput(PublishHealthProbeDirectory)
                 .EnableContinuousIntegrationBuild();

            if (noRestore)
                s = s.EnableNoRestore();

            return s;
        });
    }

    private void PublishCliArtifacts(IEnumerable<String> runtimeIdentifiers)
    {
        var releaseDirectory = PublishCliDirectory / SemanticVersion;
        var intermediateDirectory = PublishCliDirectory / "intermediate";

        PublishCliDirectory.CreateDirectory();
        releaseDirectory.CreateDirectory();

        foreach (var runtimeIdentifier in runtimeIdentifiers)
        {
            var publishDirectory = intermediateDirectory / runtimeIdentifier;
            publishDirectory.CreateOrCleanDirectory();

            DotNetPublish(s =>
            {
                s = s.SetProject(ProjectFileCli)
                     .SetConfiguration(TargetBuildConfiguration)
                     .SetRuntime(runtimeIdentifier)
                     .EnableSelfContained()
                     .EnableNoRestore()
                     .SetOutput(publishDirectory)
                     .SetAssemblyVersion(AssemblyVersion)
                     .SetFileVersion(AssemblyVersion)
                     .SetInformationalVersion(SemanticVersion)
                     .EnableContinuousIntegrationBuild();

                if (runtimeIdentifier == "linux-arm64")
                    s = s.SetProperty("ObjCopyName", "aarch64-linux-gnu-objcopy");

                return s;
            });

            var publishedExecutable = publishDirectory / GetCliPublishedExecutableName(runtimeIdentifier);
            if (!publishedExecutable.FileExists())
            {
                throw new FileNotFoundException(
                    $"CLI publish for '{runtimeIdentifier}' did not produce '{publishedExecutable}'.");
            }

            var artifact = releaseDirectory / GetCliArtifactName(runtimeIdentifier);
            File.Copy(publishedExecutable, artifact, true);
            EnsureExecutableMode(artifact, runtimeIdentifier);
            if (String.Equals(runtimeIdentifier, GetCurrentRuntimeIdentifier(), StringComparison.Ordinal))
            {
                SmokeTestPublishedCliStatus(artifact);
            }
        }

        intermediateDirectory.DeleteDirectory();
    }

    private void SmokeTestCliDockerImageCore(String platform)
    {
        var result = RunDocker(["run", "--rm", "--platform", platform, GetCliDockerImageTag(), "--version"], true);
        var output = $"{result.StandardOutput}{Environment.NewLine}{result.StandardError}";

        Assert.True(result.ExitCode == 0,
                    $"CLI image smoke test for platform '{platform}' failed with exit code {result.ExitCode}.{Environment.NewLine}{output}");

        var expectedVersion = $"ShadowDrop v{SemanticVersion}";
        Assert.True(result.StandardOutput.Contains(expectedVersion, StringComparison.Ordinal),
                    $"CLI image smoke test for platform '{platform}' did not report '{expectedVersion}'.{Environment.NewLine}{output}");
    }

    private void SmokeTestDockerImageCore(String? platform = null)
    {
        var nameSuffix = platform is null ? String.Empty : $"-{platform.Replace('/', '-')}";
        var containerName = $"shadowdrop-smoke{nameSuffix}-{Guid.NewGuid():N}";
        var volumeName = $"{containerName}-data";

        try
        {
            RunDocker(["volume", "create", volumeName]);

            var runArguments = new List<String>
            {
                "run",
                "--detach",
                "--name",
                containerName
            };

            if (platform is not null)
            {
                runArguments.Add("--platform");
                runArguments.Add(platform);
            }

            runArguments.AddRange([
                "--mount",
                $"type=volume,source={volumeName},target=/app/data",
                "--env",
                "SHADOWDROP_BOOTSTRAP_ADMIN_TOKEN=docker-smoke-test-token",
                "--publish",
                $"127.0.0.1::{DockerContainerPort}",
                GetDockerImageTag()
            ]);

            RunDocker(runArguments);

            var hostPort = GetContainerHostPort(containerName);
            WaitForHealthyContainer(containerName, new($"http://127.0.0.1:{hostPort}/health/ready"));
            AssertContainerLogsDoNotContainStartupErrors(containerName);
        }
        finally
        {
            RunDockerBestEffort(["rm", "--force", containerName]);
            RunDockerBestEffort(["volume", "rm", "--force", volumeName]);
        }
    }

    private void StageCliDockerArtifactsCore()
    {
        var releaseDirectory = PublishCliDirectory / SemanticVersion;

        DockerCliStagingDirectory.CreateOrCleanDirectory();

        foreach (var (dockerArchitecture, runtimeIdentifier) in CliDockerArchitectures)
        {
            var artifact = releaseDirectory / GetCliArtifactName(runtimeIdentifier);
            if (!artifact.FileExists())
            {
                Assert.Fail(
                    $"Linux CLI release artifact '{artifact}' is missing. Run the PublishCliLinux target, or restore the release artifact bundle into '{releaseDirectory}', before building the CLI Docker image.");
            }

            var stagedDirectory = DockerCliStagingDirectory / dockerArchitecture;
            stagedDirectory.CreateDirectory();

            var stagedExecutable = stagedDirectory / CliExecutableName;
            File.Copy(artifact, stagedExecutable, true);
            EnsureExecutableMode(stagedExecutable, runtimeIdentifier);

            Log.Information("Staged {Artifact} as {StagedExecutable}.", artifact.Name, stagedExecutable);
        }
    }

    private sealed record ProcessResult(Int32 ExitCode, String StandardOutput, String StandardError);
}
