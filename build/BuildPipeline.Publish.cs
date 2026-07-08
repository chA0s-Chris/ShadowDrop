// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.CI;

using Nuke.Common;
using Nuke.Common.IO;
using Nuke.Common.Tools.DotNet;
using Serilog;
using System.Diagnostics;
using System.Net;
using System.Text;
using static Nuke.Common.Tools.DotNet.DotNetTasks;

internal partial class BuildPipeline
{
    // The documented command users invoke; the versioned release artifact is derived from it. Independent
    // of the CLI assembly identity, which the publish output is named after.
    private const String CliExecutableName = "shadowdrop";

    // The name `dotnet publish` emits, i.e. the CLI assembly identity (the project default, since
    // ShadowDrop.Cli.csproj sets no <AssemblyName>). Renamed to CliExecutableName during publish.
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

    private static readonly String[] MacOsCliRuntimeIdentifiers =
    [
        "osx-x64",
        "osx-arm64"
    ];

    // The framework-dependent API publish output is architecture-neutral IL, so the same artifacts
    // are copied onto each platform's runtime base image to produce one tag backed by a manifest list.
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

    private static ProcessResult RunDocker(IReadOnlyCollection<String> arguments, Boolean ignoreExitCode = false) =>
        RunProcess("docker", arguments, RootDirectory, ignoreExitCode);

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
                                            Boolean ignoreExitCode = false)
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
            Log.Information("{ProcessOutput}", e.Data);
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null)
                return;

            standardError.AppendLine(e.Data);
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

        var arguments = new List<String>
        {
            "buildx",
            "build",
            "--file",
            (RootDirectory / "Dockerfile").ToString(),
            "--tag",
            GetDockerImageTag()
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

    private String GetDockerImageTag() => $"{DockerImageRepository}:{SemanticVersion}";

    private Boolean HasPublishApiArtifacts() =>
        PublishApiDirectory.DirectoryExists() && Directory.EnumerateFileSystemEntries(PublishApiDirectory).Any();

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

            if (!artifact.FileExists())
            {
                throw new FileNotFoundException(
                    $"CLI publish for '{runtimeIdentifier}' did not produce the release artifact '{artifact}'.");
            }

            EnsureExecutableMode(artifact, runtimeIdentifier);
        }

        intermediateDirectory.DeleteDirectory();
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
            WaitForHealthyContainer(containerName, new($"http://127.0.0.1:{hostPort}/health"));
            AssertContainerLogsDoNotContainStartupErrors(containerName);
        }
        finally
        {
            RunDockerBestEffort(["rm", "--force", containerName]);
            RunDockerBestEffort(["volume", "rm", "--force", volumeName]);
        }
    }

    private sealed record ProcessResult(Int32 ExitCode, String StandardOutput, String StandardError);
}
