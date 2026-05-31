#requires -Version 7
<#
.SYNOPSIS
    Builds a Velopack release package for Entra PIM Manager.

.DESCRIPTION
    Publishes the Avalonia tray app self-contained (no .NET runtime needed on
    the endpoint) and packs it with the Velopack CLI (vpk). The resulting
    package installs per-user under %LocalAppData%\Entra-PIM-Manager — no UAC, no
    HKLM, no service.

.PARAMETER Version
    Semantic version of the release, e.g. 0.1.0.

.PARAMETER SignParams
    signtool parameters for code signing. Leave empty for an unsigned (dev) build.
    Release artifacts MUST be signed with the junis GmbH code-signing certificate
    before they are promoted to a release.

.EXAMPLE
    ./build.ps1 -Version 0.1.0
#>
param(
    [string]$Version = "0.1.0",
    [string]$SignParams = ""
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$project = Join-Path $repoRoot "src\Entra-PIM-Manager.App.Avalonia\Entra-PIM-Manager.App.Avalonia.csproj"
$publishDir = Join-Path $PSScriptRoot "publish"
$releaseDir = Join-Path $PSScriptRoot "releases"

Write-Host "Building Entra PIM Manager $Version" -ForegroundColor Cyan

# 1. Ensure the Velopack CLI (vpk) is installed.
if (-not (Get-Command vpk -ErrorAction SilentlyContinue)) {
    Write-Host "Installing the Velopack CLI (vpk)..." -ForegroundColor Yellow
    dotnet tool install -g vpk
}

# 2. Publish self-contained so the endpoint needs no .NET runtime
#    (a per-user install cannot install the runtime without admin rights).
if (Test-Path $publishDir) {
    Remove-Item $publishDir -Recurse -Force
}
dotnet publish $project -c Release -r win-x64 --self-contained -o $publishDir -p:Version=$Version

# 3. Pack with Velopack.
$vpkArgs = @(
    "pack",
    "--packId", "Entra-PIM-Manager",
    "--packVersion", $Version,
    "--packDir", $publishDir,
    "--mainExe", "Entra-PIM-Manager.exe",
    "--packTitle", "Entra PIM Manager",
    "--packAuthors", "junis GmbH",
    "--icon", (Join-Path $repoRoot "src\Entra-PIM-Manager.App.Avalonia\Assets\app-icon.ico"),
    # Suppress the Desktop shortcut — Entra PIM Manager lives in the system tray
    # only, so a Desktop icon is noise. Start Menu shortcut stays so the
    # user can still find / pin the app the first time they launch it.
    "--shortcuts", "StartMenu",
    "--outputDir", $releaseDir
)

# Auto-discover release notes for this version. Drop a Markdown file at
# packaging/release-notes/{Version}.md and Velopack will surface it in the
# installer welcome screen and the .nupkg metadata.
$releaseNotesPath = Join-Path $repoRoot "packaging\release-notes\$Version.md"
if (Test-Path $releaseNotesPath) {
    Write-Host "Including release notes from $releaseNotesPath" -ForegroundColor Cyan
    $vpkArgs += @("--releaseNotes", $releaseNotesPath)
}
else {
    Write-Warning "No release notes found at $releaseNotesPath — building without."
}

if ($SignParams) {
    $vpkArgs += @("--signParams", $SignParams)
}
else {
    Write-Warning "Building UNSIGNED. Release artifacts must be signed with a code-signing certificate before promotion."
}

vpk @vpkArgs

Write-Host "Done. Release artifacts are in $releaseDir" -ForegroundColor Green
