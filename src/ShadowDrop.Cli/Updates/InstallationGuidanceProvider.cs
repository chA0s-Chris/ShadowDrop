// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Cli.Updates;

/// <summary>
/// Emits the official installer invocation documented in <c>docs/CLI.md</c> (<c>install.ps1</c> on Windows,
/// <c>install.sh</c> on Linux/macOS), pinned to the directory of the running binary so installations that
/// used a custom install directory are updated in place instead of gaining a second, stale copy on
/// <c>PATH</c>. The command is only ever printed, never executed.
/// </summary>
/// <remarks>
/// When the executable directory cannot be determined, the canonical default-directory one-liners are
/// emitted instead. Plain <c>iwr … | iex</c> cannot pass parameters, so the directory-pinned Windows
/// command uses the scriptblock form from the installation guide.
/// </remarks>
internal sealed class InstallationGuidanceProvider(Boolean isWindows, String? executableDirectory) : IInstallationGuidanceProvider
{
    private const String InstallerBaseUrl = "https://get.shadowdrop.net";

    private const String UnixDefaultInstallCommand = $"curl -fsSL {UnixInstallScriptUrl} | sh";

    private const String UnixInstallScriptUrl = $"{InstallerBaseUrl}/install.sh";

    private const String WindowsDefaultInstallCommand = $"iwr -useb {WindowsInstallScriptUrl} | iex";

    private const String WindowsInstallScriptUrl = $"{InstallerBaseUrl}/install.ps1";

    public InstallationGuidanceProvider()
        : this(OperatingSystem.IsWindows(), Path.GetDirectoryName(Environment.ProcessPath)) { }

    private static String QuotePosixShellArgument(String value) => $"'{value.Replace("'", "'\\''", StringComparison.Ordinal)}'";

    private static String QuotePowerShellArgument(String value) => $"'{value.Replace("'", "''", StringComparison.Ordinal)}'";

    public String GetInstallCommand()
    {
        if (String.IsNullOrWhiteSpace(executableDirectory))
        {
            return isWindows ? WindowsDefaultInstallCommand : UnixDefaultInstallCommand;
        }

        return isWindows
            ? $"& ([scriptblock]::Create((iwr -useb {WindowsInstallScriptUrl}))) -InstallDir {QuotePowerShellArgument(executableDirectory)}"
            : $"curl -fsSL {UnixInstallScriptUrl} | sh -s -- --install-dir {QuotePosixShellArgument(executableDirectory)}";
    }
}
