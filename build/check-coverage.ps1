#requires -Version 7
<#
.SYNOPSIS
    Enforces the Entra-PIM-Manager.Core line-coverage gate.

.DESCRIPTION
    Reads the Cobertura coverage report produced by `dotnet test --collect:"XPlat
    Code Coverage"` and fails (exit 1) when the line coverage is below the gate.
    The coverlet.runsettings file restricts measurement to [EntraPimManager.Core]*, so
    the report's overall line-rate is the Core line-rate.

.PARAMETER MinimumLineRate
    Required line-coverage fraction (0..1). Defaults to 0.70 per the build plan.

.PARAMETER ResultsDirectory
    Directory the coverage report was written to.
#>
param(
    [double]$MinimumLineRate = 0.70,
    [string]$ResultsDirectory = "./TestResults"
)

$ErrorActionPreference = "Stop"

$report = Get-ChildItem -Path $ResultsDirectory -Recurse -Filter "coverage.cobertura.xml" `
    | Sort-Object LastWriteTime -Descending `
    | Select-Object -First 1

if (-not $report) {
    Write-Error "No coverage.cobertura.xml found under $ResultsDirectory."
    exit 1
}

[xml]$xml = Get-Content -Path $report.FullName
$lineRate = [double]$xml.coverage.'line-rate'

$actual = [math]::Round($lineRate * 100, 2)
$gate = [math]::Round($MinimumLineRate * 100, 2)
Write-Host "Entra-PIM-Manager.Core line coverage: $actual% (gate: $gate%)"

if ($lineRate -lt $MinimumLineRate) {
    Write-Error "Coverage $actual% is below the required $gate%."
    exit 1
}

Write-Host "Coverage gate passed." -ForegroundColor Green
