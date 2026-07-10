& {
Param(
    [Object[]]$InstallerArguments
)

Set-StrictMode -Version 2.0; $ErrorActionPreference = "Stop"; $ConfirmPreference = "None"

function Fail([string]$Message) {
    throw "ShadowDrop installer: $Message"
}

function Download-File([string]$Uri, [string]$Destination, [string]$FailureMessage) {
    try {
        Invoke-WebRequest -Uri $Uri -OutFile $Destination -Headers $RequestHeaders -UseBasicParsing | Out-Null
    }
    catch {
        Fail "$FailureMessage`: $($_.Exception.Message)"
    }
}

function Install-StagedFile([string]$Stage, [string]$Target) {
    if (Test-Path -LiteralPath $Target -PathType Leaf) {
        try {
            [IO.File]::Replace($Stage, $Target, $null)
            return
        }
        catch {
            try {
                Move-Item -LiteralPath $Stage -Destination $Target -Force
                return
            }
            catch {
                Fail "could not replace $Target`: $($_.Exception.Message)"
            }
        }
    }

    try {
        [IO.File]::Move($Stage, $Target)
    }
    catch {
        Fail "could not install $Target`: $($_.Exception.Message)"
    }
}

$InstallDir = $null
$installDirWasBound = $false
$InstallerArguments = @($InstallerArguments)
$argumentIndex = 0
while ($argumentIndex -lt $InstallerArguments.Count) {
    $argument = [string]$InstallerArguments[$argumentIndex]
    if ($argument -ieq "-InstallDir") {
        if (($argumentIndex + 1) -ge $InstallerArguments.Count) {
            Fail "-InstallDir requires a directory"
        }
        $InstallDir = [string]$InstallerArguments[$argumentIndex + 1]
        $installDirWasBound = $true
        $argumentIndex += 2
        continue
    }
    Fail "unknown parameter: $argument"
}

if ([String]::IsNullOrWhiteSpace($InstallDir)) {
    if ($installDirWasBound) {
        Fail "-InstallDir requires a non-empty directory"
    }
    if ([String]::IsNullOrWhiteSpace($env:LOCALAPPDATA)) {
        Fail "LOCALAPPDATA is not set; use -InstallDir"
    }
    $InstallDir = [IO.Path]::Combine($env:LOCALAPPDATA, "ShadowDrop", "bin")
}

try {
    $InstallDir = [IO.Path]::GetFullPath($InstallDir)
}
catch {
    Fail "invalid install directory: $InstallDir"
}

$operatingSystem = $env:SHADOWDROP_INSTALLER_OS
if ([String]::IsNullOrWhiteSpace($operatingSystem)) {
    $operatingSystem = [Environment]::OSVersion.Platform.ToString()
}

switch ($operatingSystem.ToLowerInvariant()) {
    "windows" { }
    "win32nt" { }
    default { Fail "unsupported operating system: $operatingSystem" }
}

$architecture = $env:SHADOWDROP_INSTALLER_ARCH
if ([String]::IsNullOrWhiteSpace($architecture)) {
    try {
        $architecture = [Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()
    }
    catch {
        $architecture = $env:PROCESSOR_ARCHITEW6432
        if ([String]::IsNullOrWhiteSpace($architecture)) {
            $architecture = $env:PROCESSOR_ARCHITECTURE
        }
    }
}

if ([String]::IsNullOrWhiteSpace($architecture)) {
    Fail "could not determine the Windows architecture"
}

switch ($architecture.ToLowerInvariant()) {
    "x64" { $rid = "win-x64" }
    "amd64" { $rid = "win-x64" }
    "arm64" { $rid = "win-arm64" }
    "aarch64" { $rid = "win-arm64" }
    default { Fail "unsupported architecture: $architecture" }
}

$downloadUrl = $env:SHADOWDROP_INSTALLER_DOWNLOAD_URL
if ([String]::IsNullOrWhiteSpace($downloadUrl)) {
    $downloadUrl = "https://github.com/chA0s-Chris/ShadowDrop/releases/latest/download"
}
$downloadUrl = $downloadUrl.TrimEnd([char[]]@("/"))
$RequestHeaders = @{
    "User-Agent" = "ShadowDrop-Installer"
}
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

try {
    [IO.Directory]::CreateDirectory($InstallDir) | Out-Null
}
catch {
    Fail "could not create install directory $InstallDir`: $($_.Exception.Message)"
}

$temporaryDirectory = [IO.Path]::Combine([IO.Path]::GetTempPath(), "shadowdrop-install-$([Guid]::NewGuid().ToString("N"))")
$installStage = $null

try {
    [IO.Directory]::CreateDirectory($temporaryDirectory) | Out-Null

    $checksumFile = [IO.Path]::Combine($temporaryDirectory, "CHECKSUMS.sha256")
    Download-File "$downloadUrl/CHECKSUMS.sha256" $checksumFile "could not download CHECKSUMS.sha256 from the latest release"

    $namePattern = "-" + [Regex]::Escape($rid) + "\.exe$"
    $manifestEntries = @()
    foreach ($line in [IO.File]::ReadAllLines($checksumFile)) {
        $manifestMatch = [Regex]::Match($line, '^([0-9A-Fa-f]{64})\s+\*?(.+?)\s*$')
        if ($manifestMatch.Success -and $manifestMatch.Groups[2].Value -match $namePattern) {
            $manifestEntries += [PSCustomObject]@{
                Hash = $manifestMatch.Groups[1].Value.ToLowerInvariant()
                Name = $manifestMatch.Groups[2].Value
            }
        }
    }
    if ($manifestEntries.Count -eq 0) {
        Fail "CHECKSUMS.sha256 has no entry ending in -$rid.exe"
    }
    if ($manifestEntries.Count -gt 1) {
        Fail "CHECKSUMS.sha256 has multiple entries ending in -$rid.exe"
    }
    $binaryName = $manifestEntries[0].Name
    $expectedHash = $manifestEntries[0].Hash

    $binaryFile = [IO.Path]::Combine($temporaryDirectory, "shadowdrop.exe")
    Download-File "$downloadUrl/$binaryName" $binaryFile "could not download release asset $binaryName"

    $actualHash = (Get-FileHash -LiteralPath $binaryFile -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualHash -cne $expectedHash) {
        Fail "checksum verification failed for $binaryName"
    }

    $installStage = [IO.Path]::Combine($InstallDir, ".shadowdrop-$([Guid]::NewGuid().ToString("N")).exe")
    [IO.File]::Copy($binaryFile, $installStage, $false)
    $target = [IO.Path]::Combine($InstallDir, "shadowdrop.exe")
    Install-StagedFile $installStage $target
    $installStage = $null

    $pathContainsInstallDir = $false
    if (-not [String]::IsNullOrWhiteSpace($env:PATH)) {
        $trimCharacters = [char[]]@([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
        $normalizedInstallDir = $InstallDir.TrimEnd($trimCharacters)
        foreach ($pathEntry in ($env:PATH -split [Regex]::Escape([string][IO.Path]::PathSeparator))) {
            if (-not [String]::IsNullOrWhiteSpace($pathEntry)) {
                try {
                    $normalizedPathEntry = [IO.Path]::GetFullPath($pathEntry).TrimEnd($trimCharacters)
                }
                catch {
                    $normalizedPathEntry = $pathEntry.TrimEnd($trimCharacters)
                }
                if ($normalizedPathEntry -ieq $normalizedInstallDir) {
                    $pathContainsInstallDir = $true
                    break
                }
            }
        }
    }
    if (-not $pathContainsInstallDir) {
        Write-Warning "$InstallDir is not in PATH"
    }

    Write-Output "Installed ShadowDrop at $target"
    $versionOutput = & $target --version
    if ($LASTEXITCODE -ne 0) {
        Fail "installed ShadowDrop failed to report its version"
    }
    $versionOutput | Write-Output
}
finally {
    if (-not [String]::IsNullOrWhiteSpace($installStage) -and (Test-Path -LiteralPath $installStage)) {
        Remove-Item -LiteralPath $installStage -Force -ErrorAction SilentlyContinue
    }
    if (Test-Path -LiteralPath $temporaryDirectory) {
        Remove-Item -LiteralPath $temporaryDirectory -Recurse -Force -ErrorAction SilentlyContinue
    }
}
} -InstallerArguments $args
