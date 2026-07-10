Set-StrictMode -Version 2.0; $ErrorActionPreference = "Stop"; $ConfirmPreference = "None"

Describe "install.ps1" {
    BeforeAll {
function New-FileFixture([string]$Path) {
    [PSCustomObject]@{
        Kind = "File"
        Path = $Path
    }
}

function New-TextFixture([string]$Content) {
    [PSCustomObject]@{
        Kind = "Text"
        Content = $Content
    }
}

        $script:ProjectRoot = Split-Path $PSScriptRoot -Parent
        $script:InstallerPath = Join-Path $script:ProjectRoot "install.ps1"
        $script:InstallerContent = [IO.File]::ReadAllText($script:InstallerPath)
        $script:InstallerUrl = "https://fixture.test/install.ps1"
        $script:DownloadUrl = "https://fixture.test/download"
        $curlCommand = Get-Command curl.exe -ErrorAction SilentlyContinue
        if ($null -eq $curlCommand) {
            $curlCommand = Get-Command curl -ErrorAction Stop
        }
        $script:FixtureBinary = $curlCommand.Source
        $script:FixtureHash = (Get-FileHash -LiteralPath $script:FixtureBinary -Algorithm SHA256).Hash.ToLowerInvariant()
        $script:X64AssetName = "shadowdrop-v9.8.7-win-x64.exe"
        $script:Arm64AssetName = "shadowdrop-v9.8.7-win-arm64.exe"
        $script:UnixAssetHash = "0" * 64
        $script:ValidManifest = "$script:FixtureHash  $script:X64AssetName`r`n$script:FixtureHash  $script:Arm64AssetName`r`n$script:UnixAssetHash  shadowdrop-v9.8.7-linux-x64`r`n"
        $script:OriginalEnvironment = @{
            LOCALAPPDATA = [Environment]::GetEnvironmentVariable("LOCALAPPDATA")
            PATH = [Environment]::GetEnvironmentVariable("PATH")
            SHADOWDROP_INSTALLER_DOWNLOAD_URL = [Environment]::GetEnvironmentVariable("SHADOWDROP_INSTALLER_DOWNLOAD_URL")
            SHADOWDROP_INSTALLER_OS = [Environment]::GetEnvironmentVariable("SHADOWDROP_INSTALLER_OS")
            SHADOWDROP_INSTALLER_ARCH = [Environment]::GetEnvironmentVariable("SHADOWDROP_INSTALLER_ARCH")
        }
    }

    BeforeEach {
        $script:TestRoot = Join-Path $TestDrive ([Guid]::NewGuid().ToString("N"))
        [IO.Directory]::CreateDirectory($script:TestRoot) | Out-Null
        $env:LOCALAPPDATA = Join-Path $script:TestRoot "local"
        $env:PATH = $script:OriginalEnvironment.PATH
        $env:SHADOWDROP_INSTALLER_DOWNLOAD_URL = $script:DownloadUrl
        $env:SHADOWDROP_INSTALLER_OS = "Windows"
        $env:SHADOWDROP_INSTALLER_ARCH = "X64"
        $global:ShadowDropInstallerRequests = @()
        $global:ShadowDropInstallerWebFixtures = @{}
        $global:ShadowDropInstallerWebFixtures[$script:InstallerUrl] = New-TextFixture $script:InstallerContent
        $global:ShadowDropInstallerWebFixtures["$script:DownloadUrl/CHECKSUMS.sha256"] = New-TextFixture $script:ValidManifest
        $global:ShadowDropInstallerWebFixtures["$script:DownloadUrl/$script:X64AssetName"] = New-FileFixture $script:FixtureBinary
        $global:ShadowDropInstallerWebFixtures["$script:DownloadUrl/$script:Arm64AssetName"] = New-FileFixture $script:FixtureBinary

        Mock Invoke-WebRequest {
            param($Uri, $OutFile, $Headers, $UseBasicParsing)
            $key = [string]$Uri
            $global:ShadowDropInstallerRequests += [PSCustomObject]@{
                Uri = $key
                OutFile = [string]$OutFile
                UseBasicParsing = [bool]$UseBasicParsing
            }
            if (-not $global:ShadowDropInstallerWebFixtures.ContainsKey($key)) {
                throw "No fixture for $key"
            }
            $fixture = $global:ShadowDropInstallerWebFixtures[$key]
            if (-not [String]::IsNullOrWhiteSpace([string]$OutFile)) {
                if ($fixture.Kind -eq "File") {
                    [IO.File]::Copy($fixture.Path, $OutFile, $true)
                }
                elseif ($fixture.Kind -eq "Text") {
                    [IO.File]::WriteAllText($OutFile, $fixture.Content)
                }
                else {
                    throw "Fixture for $key cannot be downloaded"
                }
                return
            }
            if ($fixture.Kind -eq "Text") {
                return $fixture.Content
            }
            throw "Fixture for $key requires -OutFile"
        }
    }

    AfterAll {
        $env:LOCALAPPDATA = $script:OriginalEnvironment.LOCALAPPDATA
        $env:PATH = $script:OriginalEnvironment.PATH
        $env:SHADOWDROP_INSTALLER_DOWNLOAD_URL = $script:OriginalEnvironment.SHADOWDROP_INSTALLER_DOWNLOAD_URL
        $env:SHADOWDROP_INSTALLER_OS = $script:OriginalEnvironment.SHADOWDROP_INSTALLER_OS
        $env:SHADOWDROP_INSTALLER_ARCH = $script:OriginalEnvironment.SHADOWDROP_INSTALLER_ARCH
        Remove-Variable -Name ShadowDropInstallerRequests -Scope Global -ErrorAction SilentlyContinue
        Remove-Variable -Name ShadowDropInstallerWebFixtures -Scope Global -ErrorAction SilentlyContinue
    }

    It "supports the documented iwr pipe and installs the latest stable release to the default directory" {
        $defaultInstall = Join-Path $env:LOCALAPPDATA "ShadowDrop\bin"
        $env:PATH = "$defaultInstall$([IO.Path]::PathSeparator)$env:PATH"

        $output = @(iwr -useb $script:InstallerUrl | iex)

        $target = Join-Path $defaultInstall "shadowdrop.exe"
        $target | Should -Exist
        ($output -join "`n") | Should -Match "Installed ShadowDrop at"
        ($output -join "`n") | Should -Match "curl"
        @($global:ShadowDropInstallerRequests.Uri) | Should -Contain "$script:DownloadUrl/CHECKSUMS.sha256"
        @($global:ShadowDropInstallerRequests.Uri) | Should -Contain "$script:DownloadUrl/$script:X64AssetName"
        @($global:ShadowDropInstallerRequests | Where-Object { -not $_.UseBasicParsing }).Count | Should -Be 0
    }

    It "keeps the documented iwr pipe isolated from caller session state" {
        $defaultInstall = Join-Path $env:LOCALAPPDATA "ShadowDrop\bin"
        $env:PATH = "$defaultInstall$([IO.Path]::PathSeparator)$env:PATH"
        $InstallDir = "caller-install-dir"
        $RequestHeaders = "caller-request-headers"
        $ErrorActionPreference = "Continue"
        $ConfirmPreference = "High"
        Set-StrictMode -Off

        iwr -useb $script:InstallerUrl | iex | Out-Null

        $InstallDir | Should -BeExactly "caller-install-dir"
        $RequestHeaders | Should -BeExactly "caller-request-headers"
        $ErrorActionPreference | Should -BeExactly "Continue"
        $ConfirmPreference | Should -BeExactly "High"
        $strictModeAllowsUndefinedVariables = $true
        try {
            $null = $shadowDropInstallerUndefinedVariableProbe
        }
        catch {
            $strictModeAllowsUndefinedVariables = $false
        }
        $strictModeAllowsUndefinedVariables | Should -BeTrue
        Get-Command Download-File -CommandType Function -ErrorAction SilentlyContinue | Should -BeNullOrEmpty
    }

    It "supports ScriptBlock Create parameters and installs to the override directory" {
        $installDir = Join-Path $script:TestRoot "override"

        $output = @(& ([scriptblock]::Create((iwr -useb $script:InstallerUrl))) -InstallDir $installDir *>&1)

        (Join-Path $installDir "shadowdrop.exe") | Should -Exist
        ($output -join "`n") | Should -Match "curl"
        @($global:ShadowDropInstallerRequests.Uri) | Should -Contain "$script:DownloadUrl/$script:X64AssetName"
    }

    It "maps <Architecture> to <AssetName>" -ForEach @(
        @{ Architecture = "X64"; AssetName = "shadowdrop-v9.8.7-win-x64.exe" }
        @{ Architecture = "Arm64"; AssetName = "shadowdrop-v9.8.7-win-arm64.exe" }
    ) {
        $env:SHADOWDROP_INSTALLER_ARCH = $Architecture
        $installDir = Join-Path $script:TestRoot $Architecture

        & $script:InstallerPath -InstallDir $installDir *>&1 | Out-Null

        @($global:ShadowDropInstallerRequests.Uri) | Should -Contain "$script:DownloadUrl/$AssetName"
    }

    It "preserves an existing target and removes same-directory staging after a checksum mismatch" {
        $installDir = Join-Path $script:TestRoot "mismatch"
        [IO.Directory]::CreateDirectory($installDir) | Out-Null
        $target = Join-Path $installDir "shadowdrop.exe"
        [IO.File]::Copy($script:FixtureBinary, $target)
        [IO.File]::AppendAllText($target, "old")
        $existingHash = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash
        $global:ShadowDropInstallerWebFixtures["$script:DownloadUrl/CHECKSUMS.sha256"] = New-TextFixture "$script:UnixAssetHash  $script:X64AssetName`r`n"

        { & $script:InstallerPath -InstallDir $installDir } | Should -Throw "*checksum verification failed*"

        (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash | Should -BeExactly $existingHash
        @(Get-ChildItem -LiteralPath $installDir -Filter ".shadowdrop-*.exe").Count | Should -Be 0
    }

    It "replaces an existing target from a same-directory stage" {
        $installDir = Join-Path $script:TestRoot "replace"
        [IO.Directory]::CreateDirectory($installDir) | Out-Null
        $target = Join-Path $installDir "shadowdrop.exe"
        [IO.File]::Copy($script:FixtureBinary, $target)
        [IO.File]::AppendAllText($target, "old")
        $oldHash = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash
        $env:PATH = "$installDir$([IO.Path]::PathSeparator)$env:PATH"

        $output = @(& $script:InstallerPath -InstallDir $installDir *>&1)

        (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash | Should -Not -Be $oldHash
        (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLowerInvariant() | Should -BeExactly $script:FixtureHash
        @(Get-ChildItem -LiteralPath $installDir -Filter ".shadowdrop-*.exe").Count | Should -Be 0
        ($output -join "`n") | Should -Match "curl"
    }

    It "warns when the install directory is absent from PATH and prints the installed version" {
        $installDir = Join-Path $script:TestRoot "not-on-path"

        $output = @(& $script:InstallerPath -InstallDir $installDir *>&1)

        ($output -join "`n") | Should -Match ([Regex]::Escape("$installDir is not in PATH"))
        ($output -join "`n") | Should -Match "curl"
    }

    It "reports a missing release checksum manifest" {
        $global:ShadowDropInstallerWebFixtures.Remove("$script:DownloadUrl/CHECKSUMS.sha256")

        { & $script:InstallerPath -InstallDir (Join-Path $script:TestRoot "no-release") } | Should -Throw "*could not download CHECKSUMS.sha256 from the latest release*"
    }

    It "reports a manifest without a runtime-identifier entry" {
        $global:ShadowDropInstallerWebFixtures["$script:DownloadUrl/CHECKSUMS.sha256"] = New-TextFixture "$script:FixtureHash  $script:X64AssetName.extra`r`n$script:FixtureHash  $script:Arm64AssetName`r`n"

        { & $script:InstallerPath -InstallDir (Join-Path $script:TestRoot "no-entry") } | Should -Throw "*CHECKSUMS.sha256 has no entry ending in -win-x64.exe*"
    }

    It "reports a manifest with multiple runtime-identifier entries" {
        $global:ShadowDropInstallerWebFixtures["$script:DownloadUrl/CHECKSUMS.sha256"] = New-TextFixture "$script:FixtureHash  $script:X64AssetName`r`n$script:UnixAssetHash  shadowdrop-v9.9.0-win-x64.exe`r`n"

        { & $script:InstallerPath -InstallDir (Join-Path $script:TestRoot "duplicate-entries") } | Should -Throw "*CHECKSUMS.sha256 has multiple entries ending in -win-x64.exe*"
    }

    It "reports a manifest entry without a downloadable asset" {
        $global:ShadowDropInstallerWebFixtures.Remove("$script:DownloadUrl/$script:X64AssetName")

        { & $script:InstallerPath -InstallDir (Join-Path $script:TestRoot "missing-asset") } | Should -Throw "*could not download release asset $script:X64AssetName*"
    }

    It "rejects invalid parameters and unsupported host values" {
        { & $script:InstallerPath -Unknown } | Should -Throw
        { & $script:InstallerPath -PreRelease } | Should -Throw "*unknown parameter: -PreRelease*"
        { & $script:InstallerPath -InstallDir "" } | Should -Throw "*-InstallDir requires a non-empty directory*"

        $env:SHADOWDROP_INSTALLER_OS = "Linux"
        { & $script:InstallerPath -InstallDir (Join-Path $script:TestRoot "linux") } | Should -Throw "*unsupported operating system: Linux*"

        $env:SHADOWDROP_INSTALLER_OS = "Windows"
        $env:SHADOWDROP_INSTALLER_ARCH = "s390x"
        { & $script:InstallerPath -InstallDir (Join-Path $script:TestRoot "s390x") } | Should -Throw "*unsupported architecture: s390x*"
    }
}
