// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests;

using FluentAssertions;
using NUnit.Framework;
using ShadowDrop.CI;

[TestFixture]
public sealed class ElfIsaGuardTests
{
    private const String ArtifactPath = "/artifacts/shadowdrop-0.9.1-linux-x64";

    // Modelled on `readelf -n` of the 0.9.0 release binary.
    private const String BaselineOnlyNotes = """
                                             Displaying notes found in: .note.gnu.property
                                               Owner                Data size	Description
                                               GNU                  0x00000020	NT_GNU_PROPERTY_TYPE_0
                                                   Properties: x86 ISA needed: x86-64-baseline
                                                   x86 feature used: x86
                                                   x86 ISA used: x86-64-baseline
                                             """;

    private static readonly TestCaseData[] FailingNotes =
    [
        new TestCaseData(V3Notes, new[]
        {
            "x86-64-v2",
            "x86-64-v3"
        }).SetName("Baseline, v2 and v3"),
        new TestCaseData("Properties: x86 ISA needed: x86-64-v3", new[] { "x86-64-v3" }).SetName("v3 alone"),
        new TestCaseData(SeparateNeededLinesNotes, new[] { "x86-64-v2" }).SetName("Level on a later needed line"),
        new TestCaseData("Properties: x86 ISA needed: x86-64-baseline, x86-64-v4\r\n", new[] { "x86-64-v4" }).SetName("v4 with Windows line endings")
    ];

    private const String NoIsaNotes = """
                                      Displaying notes found in: .note.gnu.build-id
                                        Owner                Data size	Description
                                        GNU                  0x00000014	NT_GNU_BUILD_ID (unique build ID bitstring)
                                          Build ID: 0123456789abcdef0123456789abcdef01234567
                                      """;

    private static readonly TestCaseData[] PassingNotes =
    [
        new TestCaseData(BaselineOnlyNotes).SetName("Baseline only"),
        new TestCaseData(NoIsaNotes).SetName("No ISA note"),
        new TestCaseData(UsedOnlyNotes).SetName("Only ISA used levels above baseline"),
        new TestCaseData(String.Empty).SetName("Empty output")
    ];

    private const String SeparateNeededLinesNotes = """
                                                    Displaying notes found in: .note.gnu.property
                                                      Properties: x86 ISA needed: x86-64-baseline
                                                      Properties: x86 ISA needed: x86-64-v2
                                                    """;

    private const String UsedOnlyNotes = """
                                         Displaying notes found in: .note.gnu.property
                                           Owner                Data size	Description
                                           GNU                  0x00000020	NT_GNU_PROPERTY_TYPE_0
                                               Properties: x86 feature used: x86
                                               x86 ISA used: x86-64-baseline, x86-64-v2, x86-64-v3
                                         """;

    // Modelled on `readelf -n` of the 0.9.1 release binary built against the amd64v3 libc.
    private const String V3Notes = """
                                   Displaying notes found in: .note.gnu.property
                                     Owner                Data size	Description
                                     GNU                  0x00000020	NT_GNU_PROPERTY_TYPE_0
                                         Properties: x86 ISA needed: x86-64-baseline, x86-64-v2, x86-64-v3
                                         x86 feature used: x86
                                         x86 ISA used: x86-64-baseline, x86-64-v2, x86-64-v3
                                   """;

    [Test]
    public void ParseNeededLevels_ShouldIgnoreIsaUsedLevels()
    {
        var levels = ElfIsaGuard.ParseNeededLevels(V3Notes);

        levels.Should().Equal("x86-64-baseline", "x86-64-v2", "x86-64-v3");
    }

    [Test]
    public void ParseNeededLevels_ShouldReturnEmpty_WhenNoNeededNoteExists()
    {
        var levels = ElfIsaGuard.ParseNeededLevels(UsedOnlyNotes);

        levels.Should().BeEmpty();
    }

    [Test]
    public void Verify_ShouldFail_WhenNeitherToolIsAvailable()
    {
        var tools = new ReadNotesSpy(new Dictionary<String, String?>());

        var verify = () => ElfIsaGuard.Verify(ArtifactPath, tools.ReadNotes);

        verify.Should().Throw<InvalidOperationException>()
              .WithMessage("*llvm-readelf*readelf*")
              .WithMessage($"*{ArtifactPath}*");
        tools.RequestedTools.Should().Equal("llvm-readelf", "readelf");
    }

    [Test]
    public void Verify_ShouldNotCallReadelf_WhenLlvmReadelfIsAvailable()
    {
        var tools = new ReadNotesSpy(new Dictionary<String, String?>
        {
            ["llvm-readelf"] = BaselineOnlyNotes,
            ["readelf"] = V3Notes
        });

        ElfIsaGuard.Verify(ArtifactPath, tools.ReadNotes);

        tools.RequestedTools.Should().Equal("llvm-readelf");
    }

    [Test]
    public void Verify_ShouldPropagateToolFailure_InsteadOfFallingBack()
    {
        var tools = new ReadNotesSpy(new Dictionary<String, String?>
        {
            ["readelf"] = BaselineOnlyNotes
        })
        {
            FailingTool = "llvm-readelf"
        };

        var verify = () => ElfIsaGuard.Verify(ArtifactPath, tools.ReadNotes);

        verify.Should().Throw<IOException>();
        tools.RequestedTools.Should().Equal("llvm-readelf");
    }

    [Test]
    public void Verify_ShouldUseReadelf_WhenLlvmReadelfIsMissing()
    {
        var tools = new ReadNotesSpy(new Dictionary<String, String?>
        {
            ["readelf"] = V3Notes
        });

        var verify = () => ElfIsaGuard.Verify(ArtifactPath, tools.ReadNotes);

        verify.Should().Throw<InvalidOperationException>().WithMessage("*x86-64-v3*");
        tools.RequestedTools.Should().Equal("llvm-readelf", "readelf");
    }

    [TestCaseSource(nameof(FailingNotes))]
    public void Verify_ShouldFailNamingArtifactAndLevels_WhenLevelAboveBaselineIsNeeded(String notes, String[] expectedLevels)
    {
        var tools = new ReadNotesSpy(new Dictionary<String, String?>
        {
            ["llvm-readelf"] = notes
        });

        var verify = () => ElfIsaGuard.Verify(ArtifactPath, tools.ReadNotes);

        verify.Should().Throw<InvalidOperationException>()
              .WithMessage($"*{ArtifactPath}*levels: {String.Join(", ", expectedLevels)})*");
    }

    [TestCaseSource(nameof(PassingNotes))]
    public void Verify_ShouldPass_WhenNoLevelAboveBaselineIsNeeded(String notes)
    {
        var tools = new ReadNotesSpy(new Dictionary<String, String?>
        {
            ["llvm-readelf"] = notes
        });

        var verify = () => ElfIsaGuard.Verify(ArtifactPath, tools.ReadNotes);

        verify.Should().NotThrow();
        tools.RequestedArtifacts.Should().Equal(ArtifactPath);
    }

    private sealed class ReadNotesSpy
    {
        private readonly IReadOnlyDictionary<String, String?> _installedTools;

        public ReadNotesSpy(IReadOnlyDictionary<String, String?> installedTools)
        {
            _installedTools = installedTools;
        }

        public String? FailingTool { get; init; }

        public List<String> RequestedArtifacts { get; } = [];

        public List<String> RequestedTools { get; } = [];

        public String? ReadNotes(String tool, String artifactPath)
        {
            RequestedTools.Add(tool);
            RequestedArtifacts.Add(artifactPath);

            if (tool == FailingTool)
                throw new IOException($"{tool} exited with code 1.");

            return _installedTools.GetValueOrDefault(tool);
        }
    }
}
