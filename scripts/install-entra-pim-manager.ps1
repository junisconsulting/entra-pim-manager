#requires -Version 5.1

<#
.SYNOPSIS
    Installs Entra PIM Manager and pins one App Registration to a tenant, unattended.

.DESCRIPTION
    The endpoint half of a rollout. Runs the Velopack installer silently, then calls the
    installed executable to write the tenant registration, and verifies that the entry
    actually landed in the per-user configuration.

    Run this in the *user's* context - both the install and the configuration are
    per-user. A run as SYSTEM writes into the SYSTEM profile and the user ends up with
    an unconfigured app.

    For a self-hosted App Registration, get the tenant and client id from
    scripts/create-app-registration.ps1, which an admin runs once per tenant. Without
    -ClientId the tenant is junis-hosted: nothing is created, an admin of the tenant
    consents once. Details: docs/unattended-deployment.md.

.PARAMETER TenantId
    Directory (tenant) id to configure. Required.

.PARAMETER ClientId
    Application (client) id of the tenant's own App Registration (self-hosted). Optional:
    without it, the tenant is junis-hosted, and the script reads the junis client id from
    the installed app. junis-hosted exists for the Global cloud only, so -Cloud China
    requires -ClientId. An installed version that does not offer junis-hosted yet stops
    the script.

    Re-running the script switches the tenant: without -ClientId to junis-hosted, with
    -ClientId back to self-hosted. Remove the tenant's accounts in the app first - the
    script cannot, and they would otherwise show as no longer valid while the old
    registration's tokens stay with Windows.

.PARAMETER SetupExe
    Path to Entra-PIM-Manager-win-Setup.exe. Omit to configure an existing install
    without reinstalling.

.PARAMETER Cloud
    Global or China.

.PARAMETER Label
    Optional alias for the tenant, shown in the app's sign-in picker and tenant list.

.PARAMETER TicketSystem
    Optional ticketing system for this tenant, e.g. "ServiceNow". Prefilled into the
    activation form for every user in the tenant. Stored in settings.json rather than
    with the registration; like the registration it takes effect at the next app start.

.PARAMETER LogoPath
    Optional PNG with your organization's logo, shown at the top right of the panel. Copied into
    the per-user data folder as logo.png; takes effect at the next app start. PNG only.

.PARAMETER NoStart
    Do not launch the app when finished. Use this for an unattended rollout that should
    not put a tray icon in front of the user mid-install - autostart is already set, so
    it comes up configured at the next logon either way.

.EXAMPLE
    ./install-entra-pim-manager.ps1 -SetupExe .\Entra-PIM-Manager-win-Setup.exe `
        -TenantId 00000000-0000-0000-0000-000000000001

    junis-hosted: no -ClientId.

.EXAMPLE
    ./install-entra-pim-manager.ps1 -SetupExe .\Entra-PIM-Manager-win-Setup.exe `
        -TenantId 00000000-0000-0000-0000-000000000001 `
        -ClientId 00000000-0000-0000-0000-000000000002

    self-hosted: the tenant's own App Registration.

.EXAMPLE
    ./install-entra-pim-manager.ps1 -TenantId <guid> -ClientId <guid> `
        -Label "Contoso" -TicketSystem "ServiceNow"
#>
param(
    [string]$TenantId = "",
    [string]$ClientId = "",
    [string]$SetupExe = "",
    [string]$Cloud = "Global",
    [string]$Label = "",
    [string]$TicketSystem = "",
    [string]$LogoPath = "",
    [switch]$NoStart
)

$ErrorActionPreference = "Stop"

$installRoot = Join-Path $env:LOCALAPPDATA "Entra-PIM-Manager"
$dataDir = Join-Path $env:LOCALAPPDATA "junis\Entra-PIM-Manager"
$configFile = Join-Path $dataDir "appsettings.local.json"

# 1. Validate here, where a human typed the values. The app rejects a bad id with exit
#    code 1 and writes nothing, but a message at this point is worth more than a code.
if (-not ($TenantId -as [guid])) {
    throw "TenantId '$TenantId' is not a GUID. Pass -TenantId <guid>."
}

# No -ClientId means junis-hosted, which exists for the Global cloud only. Its id is read
# after the install, from the version that will sign in with it (step 3).
if ($ClientId) {
    if (-not ($ClientId -as [guid])) {
        throw "ClientId '$ClientId' is not a GUID. Pass -ClientId <guid>."
    }
}
elseif ($Cloud -eq "China") {
    throw "Entra China needs your own App Registration. Pass -ClientId <guid>."
}

if ($Cloud -notin @("Global", "China")) {
    throw "Cloud must be 'Global' or 'China', got '$Cloud'."
}

# The app shows nothing for a file it cannot decode and only logs why, so an SVG would
# fail silently on every device. Say it here instead.
if ($LogoPath -and ([IO.Path]::GetExtension($LogoPath) -ne ".png" -or -not (Test-Path -LiteralPath $LogoPath -PathType Leaf))) {
    throw "LogoPath '$LogoPath' is not an existing .png file. The app shows PNG logos only."
}

# Pass and compare the canonical form. The app accepts any GUID format but stores the tenant
# id as Guid.ToString(), so a braced or 32-digit tenant id would be written fine and then fail
# the check in step 5 - an install Intune would report as failed and retry. The client id it
# stores as given, so a braced one would reach sign-in with its braces.
$TenantId = ([guid]$TenantId).ToString()

# 2. Install. Silent means no dialogs - and no app launch either, which is exactly why
#    the configuration below is a second, separate call.
if ($SetupExe) {
    if (-not (Test-Path $SetupExe)) {
        throw "Installer not found: $SetupExe"
    }

    Write-Host "Installing Entra PIM Manager..." -ForegroundColor Cyan
    $setup = Start-Process -FilePath (Resolve-Path $SetupExe) -ArgumentList "--silent" -Wait -PassThru
    if ($setup.ExitCode -ne 0) {
        throw "The installer failed with exit code $($setup.ExitCode)."
    }
}
else {
    Write-Host "No -SetupExe given, configuring the existing installation." -ForegroundColor DarkGray
}

# 3. Prefer current\, which is the executable Velopack's own install hook invokes and
#    which it replaces in place on update. The launcher stub one level up forwards to
#    the same binary, but that it forwards arguments is unverified - so it is only the
#    fallback for a layout that has no current\ directory.
$exe = Join-Path $installRoot "current\Entra-PIM-Manager.exe"
if (-not (Test-Path $exe)) {
    $exe = Join-Path $installRoot "Entra-PIM-Manager.exe"
}

if (-not (Test-Path $exe)) {
    throw "Entra PIM Manager is not installed under $installRoot. Pass -SetupExe to install it."
}

#    junis-hosted: the junis client id ships in the app's own appsettings.json. Read it from
#    current\, where the app and its configuration live - the stub folder has no copy. An
#    older version has no id there, and the app ignores any value that is not a GUID.
if (-not $ClientId) {
    $ClientId = (Get-Content (Join-Path $installRoot "current\appsettings.json") -Raw -Encoding UTF8 | ConvertFrom-Json).EntraPimManager.JunisHostedClientId
    if (-not ($ClientId -as [guid])) {
        throw "This installation does not offer junis-hosted yet; pass -ClientId or install a newer version."
    }
}

# Canonical, like the tenant id above: either way, given or read.
$ClientId = ([guid]$ClientId).ToString()

# 4. Configure. Start-Process -Wait is required: the app is a GUI executable, so a plain
#    call returns before it has done anything and the exit code would be lost.
#
#    Build ONE quoted command line rather than an array: Start-Process joins an
#    -ArgumentList array with spaces and quotes nothing, so -Label "junis DEV" would
#    reach the app as two arguments and it would store just "junis".
$quote = { param($value) '"' + $value.Replace('"', '\"') + '"' }

$argumentLine = "--tenant-id $TenantId --client-id $ClientId"
if ($Cloud -ne "Global") {
    $argumentLine += " --cloud $Cloud"
}

if ($Label) {
    $argumentLine += " --label $(& $quote $Label)"
}

if ($TicketSystem) {
    $argumentLine += " --ticket-system $(& $quote $TicketSystem)"
}

Write-Host "Configuring tenant $TenantId..." -ForegroundColor Cyan
$configure = Start-Process -FilePath $exe -ArgumentList $argumentLine -Wait -PassThru

switch ($configure.ExitCode) {
    0 { }
    1 { throw "The app rejected the arguments (exit code 1). Nothing was written." }
    2 { throw "The app could not write $configFile (exit code 2). Check whether it exists and is valid JSON." }
    default { throw "Unexpected exit code $($configure.ExitCode). An installation older than 0.10.0 does not know these arguments and starts the tray app instead." }
}

# 5. Trust the file, not the exit code - this is also what proves the arguments reached
#    the app rather than being swallowed by a launcher.
if (-not (Test-Path $configFile)) {
    throw "Exit code 0 but $configFile does not exist. The arguments did not reach the app."
}

#    -Encoding UTF8: the app writes the file without a BOM, and Windows PowerShell 5.1 would
#    otherwise decode it with the ANSI code page, which breaks a non-ASCII label under a DBCS locale.
$written = (Get-Content $configFile -Raw -Encoding UTF8 | ConvertFrom-Json).EntraPimManager.TenantAppRegistrations |
    Where-Object { $_.TenantId -eq $TenantId -and $_.ClientId -eq $ClientId }

if (-not $written) {
    throw "No entry for tenant $TenantId in $configFile. The arguments did not reach the app."
}

# 6. The logo goes next to the configuration the app just wrote, so the folder exists.
#    The app reads it at startup - the restart below picks it up too.
#    Clear read-only: Copy-Item carries the attribute over, and the app's Remove and the
#    uninstall could no longer delete the file.
if ($LogoPath) {
    (Copy-Item -LiteralPath $LogoPath (Join-Path $dataDir "logo.png") -Force -PassThru).IsReadOnly = $false
}

Write-Host ""
Write-Host "Done. Tenant $TenantId is configured for cloud $Cloud." -ForegroundColor Green

# 7. Start the app so the result is visible at once. An already-running instance still
#    holds the configuration it read at ITS startup - the registration list is bound with
#    reloadOnChange:false - so launching a second process would only signal the old one to
#    show its window, still without the new tenant. Restart it instead.
if ($NoStart) {
    Write-Host "Not starting the app (-NoStart). It starts configured at the next logon." -ForegroundColor DarkGray
    return
}

$running = Get-Process -Name "Entra-PIM-Manager" -ErrorAction SilentlyContinue
if ($running) {
    Write-Host "Restarting the running instance so it picks up the new tenant..." -ForegroundColor Yellow
    $running | Stop-Process
    $running | Wait-Process -Timeout 10 -ErrorAction SilentlyContinue
}

Write-Host "Starting Entra PIM Manager..." -ForegroundColor Cyan
Start-Process -FilePath $exe
Write-Host "It is in the system tray. Autostart is set, so it also starts at the next logon." -ForegroundColor DarkGray
