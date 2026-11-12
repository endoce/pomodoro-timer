param([Parameter(Mandatory)][string]$PublishDirectory,
      [Parameter(Mandatory)][string]$ReportDirectory)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'portable-validation.ps1')
$PublishDirectory = (Resolve-Path -LiteralPath $PublishDirectory).Path
Assert-PortablePayload $PublishDirectory
New-Item -ItemType Directory -Force $ReportDirectory | Out-Null
Assert-VcManifest $PublishDirectory (Join-Path $ReportDirectory 'VC-RUNTIME.csv')
$vswhere = ${env:ProgramFiles(x86)} + '\Microsoft Visual Studio\Installer\vswhere.exe'
$vs = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if ($LASTEXITCODE -ne 0 -or !$vs) { throw 'Visual Studio lookup failed; native audit cannot be skipped.' }
$dumpbin = Get-ChildItem (Join-Path $vs 'VC\Tools\MSVC') -Filter dumpbin.exe -Recurse |
    Where-Object FullName -match 'Hostx64\\x64\\dumpbin.exe$' | Sort-Object FullName -Descending | Select-Object -First 1
if (!$dumpbin) { throw 'dumpbin missing; native dependency audit cannot be skipped.' }
$dependencyReport = [System.Collections.Generic.List[string]]::new()
foreach ($file in Get-PortableFiles $PublishDirectory | Where-Object Extension -in @('.exe','.dll')) {
    $output = & $dumpbin.FullName /nologo /dependents $file.FullName 2>&1
    if ($LASTEXITCODE -ne 0) { throw "PE audit failed: $($file.Name)" }
    $imports = @($output | ForEach-Object {
        if ($_ -match '^\s+([a-zA-Z0-9_.-]+\.dll)\s*$') { $Matches[1] }
    } | Sort-Object -Unique)
    $relative = [IO.Path]::GetRelativePath($PublishDirectory, $file.FullName).Replace('\','/')
    $dependencyReport.Add("${relative}: $($imports -join ', ')")
    foreach ($name in $imports | Where-Object { $_ -match '^(msvcp|vcruntime|concrt|vccorlib|vcamp)1[0-9]' }) {
        if (!(Test-Path (Join-Path $PublishDirectory $name))) { throw "App-local CRT import unresolved: $name required by $($file.Name)" }
    }
}
$dependencyReport | Set-Content (Join-Path $ReportDirectory 'NATIVE-DEPENDENCIES.txt')
# package-portable.ps1 generates/validates the manifest after summary, user guide and link checks.
Write-Output 'PASS: .NET/WPF payload, Windows App SDK metadata/runtime, app-local CRT import presence.'
Write-Output 'NOT TESTED: loader behavior without installed shared CRT, desktop notifications and tray.'
