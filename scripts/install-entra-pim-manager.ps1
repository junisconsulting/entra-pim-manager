#requires -Version 7
<#
.SYNOPSIS
    Installs Entra PIM Manager and pins one App Registration to a tenant, unattended.

.DESCRIPTION
    The endpoint half of a rollout. Runs the Velopack installer silently, then calls the
    installed executable to write the tenant registration, and verifies that the entry
    actually landed in the per-user configuration.

    Run this in the *user's* context — both the install and the configuration are
    per-user. A run as SYSTEM writes into the SYSTEM profile and the user ends up with
    an unconfigured app.

    Get the tenant and client id from scripts/create-app-registration.ps1, which an
    admin runs once per tenant. Details: docs/unattended-deployment.md.

.PARAMETER TenantId
    Directory (tenant) id to configure. Required.

.PARAMETER ClientId
    Application (client) id of the App Registration. Required.

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
    with the registration, and applies without a restart.

.PARAMETER NoStart
    Do not launch the app when finished. Use this for an unattended rollout that should
    not put a tray icon in front of the user mid-install — autostart is already set, so
    it comes up configured at the next logon either way.

.EXAMPLE
    ./install-entra-pim-manager.ps1 -SetupExe .\Entra-PIM-Manager-win-Setup.exe `
        -TenantId 00000000-0000-0000-0000-000000000001 `
        -ClientId 00000000-0000-0000-0000-000000000002

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
    [switch]$NoStart
)

$ErrorActionPreference = "Stop"

$installRoot = Join-Path $env:LOCALAPPDATA "Entra-PIM-Manager"
$configFile = Join-Path $env:LOCALAPPDATA "junis\Entra-PIM-Manager\appsettings.local.json"

# 1. Validate here, where a human typed the values. The app rejects a bad id with exit
#    code 1 and writes nothing, but a message at this point is worth more than a code.
if (-not ($TenantId -as [guid])) {
    throw "TenantId '$TenantId' is not a GUID. Pass -TenantId <guid>."
}

if (-not ($ClientId -as [guid])) {
    throw "ClientId '$ClientId' is not a GUID. Pass -ClientId <guid>."
}

if ($Cloud -notin @("Global", "China")) {
    throw "Cloud must be 'Global' or 'China', got '$Cloud'."
}

# 2. Install. Silent means no dialogs — and no app launch either, which is exactly why
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
#    the same binary, but that it forwards arguments is unverified — so it is only the
#    fallback for a layout that has no current\ directory.
$exe = Join-Path $installRoot "current\Entra-PIM-Manager.exe"
if (-not (Test-Path $exe)) {
    $exe = Join-Path $installRoot "Entra-PIM-Manager.exe"
}

if (-not (Test-Path $exe)) {
    throw "Entra PIM Manager is not installed under $installRoot. Pass -SetupExe to install it."
}

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

# 5. Trust the file, not the exit code — this is also what proves the arguments reached
#    the app rather than being swallowed by a launcher.
if (-not (Test-Path $configFile)) {
    throw "Exit code 0 but $configFile does not exist. The arguments did not reach the app."
}

$written = (Get-Content $configFile -Raw | ConvertFrom-Json).EntraPimManager.TenantAppRegistrations |
    Where-Object { $_.TenantId -eq $TenantId -and $_.ClientId -eq $ClientId }

if (-not $written) {
    throw "No entry for tenant $TenantId in $configFile. The arguments did not reach the app."
}

Write-Host ""
Write-Host "Done. Tenant $TenantId is configured for cloud $Cloud." -ForegroundColor Green

# 6. Start the app so the result is visible at once. An already-running instance still
#    holds the configuration it read at ITS startup — the registration list is bound with
#    reloadOnChange:false — so launching a second process would only signal the old one to
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
