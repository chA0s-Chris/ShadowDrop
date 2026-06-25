// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.CI;

using Nuke.Common;
using Nuke.Common.IO;
using Nuke.Common.Tools.DotNet;
using Serilog;
using static Nuke.Common.Tools.DotNet.DotNetTasks;

internal partial class BuildPipeline
{
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

    private static readonly String[] WindowsCliRuntimeIdentifiers =
    [
        "win-x64",
        "win-arm64"
    ];

    public Target Publish => target =>
        target.DependsOn(PublishApi, PublishCli);

    private Target PublishApi => target =>
        target.DependsOn(Restore)
              .After(Clean, RestoreTools)
              .Executes(() =>
              {
                  Log.Information("Publishing API...");

                  DotNetPublish(s => s
                                     .SetProject(ProjectFileApi)
                                     .SetConfiguration(TargetBuildConfiguration)
                                     .EnableNoRestore()
                                     .SetOutput(PublishApiDirectory)
                                     .EnableContinuousIntegrationBuild());
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
        return $"ShadowDrop.Cli{extension}";
    }

    private static Boolean IsWindowsRuntime(String runtimeIdentifier) =>
        runtimeIdentifier.StartsWith("win-", StringComparison.Ordinal);

    private String GetCliArtifactName(String runtimeIdentifier)
    {
        var extension = IsWindowsRuntime(runtimeIdentifier) ? ".exe" : String.Empty;
        return $"shadowdrop-cli-{SemanticVersion}-{runtimeIdentifier}{extension}";
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
            File.Copy(publishedExecutable, artifact, overwrite: true);
            EnsureExecutableMode(artifact, runtimeIdentifier);
        }

        intermediateDirectory.DeleteDirectory();
    }
}
