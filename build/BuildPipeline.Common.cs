// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.CI;

using Nuke.Common;
using Nuke.Common.Git;
using Nuke.Common.IO;
using Nuke.Common.ProjectModel;
using Nuke.Common.Tooling;
using Nuke.Common.Tools.DotNet;

internal partial class BuildPipeline
{
    [Parameter]
    public String ReleaseVersion { get; set; } = "0.1.0-dev";

    private static AbsolutePath ReleaseNotesFile => RootDirectory / "ReleaseNotes.md";

    private AbsolutePath ArtifactsDirectory => RootDirectory / "artifacts";

    private String AssemblyVersion { get; set; } = String.Empty;

    private Target Clean => target =>
        target.Before(Restore, RestoreTools, Build, /*Pack,*/ BuildTests, Test)
              .Executes(() =>
              {
                  DotNetTasks.DotNetClean(x => x.SetConfiguration(TargetBuildConfiguration)
                                                .EnableContinuousIntegrationBuild()
                                                .DisableProcessOutputLogging());

                  ArtifactsDirectory.CreateOrCleanDirectory();
                  PublishDirectory.CreateOrCleanDirectory();
              });

    private AbsolutePath CoverageDirectory => ArtifactsDirectory / "test-coverage";

    private AbsolutePath CoverageSettingsFile => RootDirectory / "coverlet.xml";

    [GitRepository]
    private GitRepository GitRepository { get; init; } = null!;

    private AbsolutePath MergedCoverageResultsFile => CoverageDirectory / "coverage.cobertura.merged.xml";

    [Parameter(Name = "NUGET_PACKAGES_DIRECTORY")]
    private AbsolutePath PackagesDirectory { get; set; } = RootDirectory / ".nuget";

    private AbsolutePath ProjectFileApi => SourceDirectory / "ShadowDrop.Api/ShadowDrop.Api.csproj";

    private AbsolutePath ProjectFileCli => SourceDirectory / "ShadowDrop.Cli/ShadowDrop.Cli.csproj";

    private AbsolutePath ProjectFileHealthProbe => SourceDirectory / "ShadowDrop.HealthProbe/ShadowDrop.HealthProbe.csproj";

    private AbsolutePath PublishApiDirectory => PublishDirectory / "api";

    private AbsolutePath PublishCliDirectory => PublishDirectory / "cli";

    private AbsolutePath PublishDirectory => ArtifactsDirectory / "publish";

    private AbsolutePath PublishHealthProbeDirectory => PublishDirectory / "health-probe";

    private String ReleaseNotes { get; set; } = String.Empty;

    private String SemanticVersion { get; set; } = String.Empty;

    [Solution]
    private Solution Solution { get; set; } = null!;

    [Parameter]
    private String TargetBuildConfiguration { get; set; } = "Release";

    private AbsolutePath TestsDirectory => RootDirectory / "tests";
}
