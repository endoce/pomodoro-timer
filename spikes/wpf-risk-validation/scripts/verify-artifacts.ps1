param([Parameter(Mandatory)][string]$ArtifactDirectory)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
foreach ($name in @('PomodoroTimer-WPF-Spike-Windows-x64.zip','PomodoroTimer-WPF-Spike-Setup.exe',
    'SHA256SUMS.txt','BUILD-INFO.txt','TEST-SUMMARY.txt')) {
    if (!(Test-Path (Join-Path $ArtifactDirectory $name))) { throw "Artifact missing: $name" }
}
foreach ($line in Get-Content (Join-Path $ArtifactDirectory 'SHA256SUMS.txt')) {
    if ($line -notmatch '^([0-9a-fA-F]{64})  ([^/\\]+)$') { throw "Invalid checksum line: $line" }
    $expected = $Matches[1]; $name = $Matches[2]
    $actual = (Get-FileHash (Join-Path $ArtifactDirectory $name) -Algorithm SHA256).Hash
    if ($actual -ne $expected) { throw "SHA256 mismatch: $name" }
}
$extract = Join-Path $ArtifactDirectory 'verify-extracted'
Expand-Archive (Join-Path $ArtifactDirectory 'PomodoroTimer-WPF-Spike-Windows-x64.zip') $extract -Force
foreach ($name in @('PomodoroTimerSpike.exe','coreclr.dll','PresentationFramework.dll','vcruntime140.dll')) {
    if (!(Test-Path (Join-Path $extract "PomodoroTimerSpike/$name"))) { throw "ZIP missing $name" }
}
# Remove only this script's generated verification extraction.
Remove-Item $extract -Recurse -Force
Write-Output 'PASS: required artifacts, ZIP contents and every listed SHA256.'
