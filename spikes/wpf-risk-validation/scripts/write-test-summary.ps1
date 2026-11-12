param([Parameter(Mandatory)][string]$ReportDirectory)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'portable-validation.ps1')
Get-PortableTestSummary $ReportDirectory | Set-Content -LiteralPath (Join-Path $ReportDirectory 'TEST-SUMMARY.txt') -Encoding utf8
Assert-TestSummary $ReportDirectory
