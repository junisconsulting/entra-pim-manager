#requires -Version 5.1
<#
.SYNOPSIS
    Wraps the Velopack Setup.exe and the endpoint scripts into an Intune Win32 app package.

.DESCRIPTION
    Produces Entra-PIM-Manager-win-Setup.intunewin. The package is tenant-neutral: the tenant
    and client id go into the install command in Intune, never into the package, so one asset
    serves every tenant. The values to enter in Intune are in docs/unattended-deployment.md;
    why a Win32 app and not an MSI is in docs/adr/0001-intune-win32-package.md.

    Uses Microsoft's IntuneWinAppUtil, downloaded at a pinned version and checked against its
    SHA256. Its license allows using it but not redistributing it, so it is never committed or
    attached to a release. Windows only: the tool needs the .NET Framework.

    The release workflow runs this after packing; run it by hand on Windows to package a
    local build for an Intune test.

.PARAMETER SetupExe
    Path to Entra-PIM-Manager-win-Setup.exe.

.PARAMETER OutputDir
    Directory the .intunewin is written to.

.EXAMPLE
    ./packaging/intune/build.ps1 -SetupExe .\Entra-PIM-Manager-win-Setup.exe -OutputDir .
#>
param(
    [Parameter(Mandatory)][string]$SetupExe,
    [Parameter(Mandatory)][string]$OutputDir
)

$ErrorActionPreference = "Stop"

$toolVersion = "1.8.7"
$toolSha256 = "C1BA45B5CB939E84AF064BB7FF4B38FB3DFE33C8DC1078FD9B157672EAE671F6"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$workDir = Join-Path $PSScriptRoot "output"
$tool = Join-Path $workDir "IntuneWinAppUtil.exe"
$content = Join-Path $workDir "content"

# 1. Fetch the packer and refuse anything but the pinned build. Microsoft ships the exe in the
#    repository rather than as a release asset, so the tag's raw URL is the download.
New-Item -ItemType Directory -Force -Path $workDir | Out-Null
$toolUrl = "https://raw.githubusercontent.com/microsoft/Microsoft-Win32-Content-Prep-Tool/v$toolVersion/IntuneWinAppUtil.exe"
Invoke-WebRequest -UseBasicParsing -Uri $toolUrl -OutFile $tool

$actualSha256 = (Get-FileHash -Algorithm SHA256 $tool).Hash
if ($actualSha256 -ne $toolSha256) {
    throw "IntuneWinAppUtil.exe does not match the pinned SHA256 (expected $toolSha256, got $actualSha256)."
}

# 2. Stage exactly the three files the package carries: the tool zips the whole folder.
if (Test-Path $content) {
    Remove-Item $content -Recurse -Force
}

New-Item -ItemType Directory -Path $content | Out-Null
Copy-Item $SetupExe (Join-Path $content "Entra-PIM-Manager-win-Setup.exe")
Copy-Item (Join-Path $repoRoot "scripts\install-entra-pim-manager.ps1") $content
Copy-Item (Join-Path $repoRoot "scripts\uninstall-entra-pim-manager.cmd") $content

# 3. Pack. -q: no prompts, create the output folder, overwrite an existing package. The output
#    is named after the setup file, which is what makes the asset Entra-PIM-Manager-win-Setup.intunewin.
& $tool -c $content -s (Join-Path $content "Entra-PIM-Manager-win-Setup.exe") -o $OutputDir -q
if ($LASTEXITCODE -ne 0) {
    throw "IntuneWinAppUtil failed with exit code $LASTEXITCODE."
}

Write-Host "Done: $(Join-Path $OutputDir 'Entra-PIM-Manager-win-Setup.intunewin')" -ForegroundColor Green
