param([Parameter(Mandatory)][string]$PublishDirectory,
      [Parameter(Mandatory)][string]$ReportDirectory)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$vswhere = ${env:ProgramFiles(x86)} + '\Microsoft Visual Studio\Installer\vswhere.exe'
$vs = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (!$vs) { throw 'Official Visual Studio C++ tools/redist directory not found.' }
$redist = Join-Path $vs 'VC\Redist\MSVC'
$candidates = @(Get-ChildItem $redist -Directory | ForEach-Object {
    $toolset = $_
    if (Test-Path (Join-Path $_.FullName 'x64')) {
        Get-ChildItem (Join-Path $_.FullName 'x64') -Directory | Where-Object Name -match '^Microsoft\.VC[0-9]+\.CRT$' |
            ForEach-Object { [pscustomobject]@{Version=[version]$toolset.Name;Path=$_.FullName} }
    }
} | Sort-Object Version -Descending)
if ($candidates.Count -eq 0) { throw 'Microsoft x64 CRT Redist directory not found; do not copy System32 DLLs.' }
$source = $candidates[0].Path
$rows = @()
foreach ($file in Get-ChildItem $source -Filter '*.dll' -File) {
    $signature = Get-AuthenticodeSignature $file.FullName
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'Microsoft') {
        throw "VC runtime is not validly Microsoft-signed: $($file.Name)"
    }
    if ($file.Name -match '^(msvcp|vcruntime|concrt|vccorlib)' -and
        $file.VersionInfo.FileVersion -match '^([0-9]+\.[0-9]+\.[0-9]+\.[0-9]+)') {
        $version = [version]$Matches[1]
        if ($version.Major -ne 14) { throw "Unexpected VC runtime major: $version" }
    }
    Copy-Item $file.FullName (Join-Path $PublishDirectory $file.Name) -Force
    $rows += [pscustomobject]@{
        File=$file.Name; Version=$file.VersionInfo.FileVersion; Source=$file.FullName
        SHA256=(Get-FileHash $file.FullName -Algorithm SHA256).Hash
    }
}
foreach ($required in @('msvcp140.dll','vcruntime140.dll','vcruntime140_1.dll')) {
    if (!(Test-Path (Join-Path $PublishDirectory $required))) { throw "Missing app-local $required" }
}
New-Item -ItemType Directory -Force $ReportDirectory | Out-Null
$rows | Export-Csv (Join-Path $ReportDirectory 'VC-RUNTIME.csv') -NoTypeInformation -Encoding utf8
@'
VC++ v14 runtime: app-local deployment from signed Visual Studio Redist/MSVC/x64/Microsoft.VC*.CRT.
No global vc_redist installation or application installer; unzip and run as a normal user.
Windows Universal CRT is an OS component, not copied from System32.
This does NOT prove a clean Windows 11 25H2 desktop has been tested.
Missing DLL/symbol behavior and clean-machine operation require desktop acceptance.
App-local runtime servicing is the application's responsibility; check redistribution license terms.
https://learn.microsoft.com/en-us/cpp/windows/redistributing-visual-cpp-files
https://learn.microsoft.com/en-us/cpp/windows/deployment-in-visual-cpp
'@ | Set-Content (Join-Path $ReportDirectory 'VC-RUNTIME.txt')
$rows | Format-Table File, Version
