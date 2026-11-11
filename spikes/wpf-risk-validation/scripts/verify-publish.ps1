param([Parameter(Mandatory)][string]$PublishDirectory,
      [Parameter(Mandatory)][string]$ReportDirectory)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$required = @('PomodoroTimerSpike.exe','PomodoroTimerSpike.dll',
    'PomodoroTimerSpike.runtimeconfig.json','PomodoroTimerSpike.deps.json',
    'coreclr.dll','hostfxr.dll','hostpolicy.dll','System.Private.CoreLib.dll',
    'PresentationFramework.dll','PresentationCore.dll','WindowsBase.dll',
    'wpfgfx_cor3.dll','PresentationNative_cor3.dll',
    'msvcp140.dll','vcruntime140.dll','vcruntime140_1.dll')
foreach ($name in $required) {
    if (!(Test-Path (Join-Path $PublishDirectory $name))) { throw "Required publish dependency missing: $name" }
}
$exe = [IO.File]::ReadAllBytes((Join-Path $PublishDirectory 'PomodoroTimerSpike.exe'))
$peOffset = [BitConverter]::ToInt32($exe, 0x3c)
if ([BitConverter]::ToUInt16($exe, $peOffset + 4) -ne 0x8664) { throw 'Published apphost is not AMD64.' }
if (!(Get-ChildItem $PublishDirectory -Recurse -Filter '*WindowsAppRuntime*.dll')) {
    throw 'Windows App SDK native runtime payload missing.'
}
if (!(Get-ChildItem $PublishDirectory -Recurse -Filter '*.winmd')) {
    throw 'Windows App SDK runtime metadata missing.'
}
$config = Get-Content (Join-Path $PublishDirectory 'PomodoroTimerSpike.runtimeconfig.json') -Raw | ConvertFrom-Json
if ($config.runtimeOptions.PSObject.Properties.Name -contains 'framework' -or
    $config.runtimeOptions.PSObject.Properties.Name -contains 'frameworks') {
    throw 'Runtime config still requires a shared .NET framework.'
}
foreach ($framework in @('Microsoft.NETCore.App','Microsoft.WindowsDesktop.App')) {
    $actual = @($config.runtimeOptions.includedFrameworks | Where-Object name -eq $framework)
    if ($actual.Count -ne 1 -or $actual[0].version -ne '10.0.14') {
        throw "Unexpected self-contained runtime version for $framework"
    }
}
New-Item -ItemType Directory -Force $ReportDirectory | Out-Null
$vswhere = ${env:ProgramFiles(x86)} + '\Microsoft Visual Studio\Installer\vswhere.exe'
$vs = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
$dumpbin = Get-ChildItem (Join-Path $vs 'VC\Tools\MSVC') -Filter dumpbin.exe -Recurse |
    Where-Object FullName -match 'Hostx64\\x64\\dumpbin.exe$' | Sort-Object FullName -Descending | Select-Object -First 1
if (!$dumpbin) { throw 'dumpbin missing; native dependency audit cannot be skipped.' }
$dependencyReport = [System.Collections.Generic.List[string]]::new()
foreach ($file in Get-ChildItem $PublishDirectory -Recurse -File | Where-Object Extension -in @('.exe','.dll')) {
    $output = & $dumpbin.FullName /nologo /dependents $file.FullName 2>&1
    if ($LASTEXITCODE -ne 0) { throw "PE audit failed: $($file.Name)" }
    $imports = @($output | ForEach-Object {
        if ($_ -match '^\s+([a-zA-Z0-9_.-]+\.dll)\s*$') { $Matches[1] }
    } | Sort-Object -Unique)
    $dependencyReport.Add("$($file.FullName): $($imports -join ', ')")
    foreach ($name in $imports | Where-Object { $_ -match '^(msvcp|vcruntime|concrt|vccorlib|vcamp)1[0-9]' }) {
        if (!(Test-Path (Join-Path $PublishDirectory $name))) { throw "App-local CRT import unresolved: $name required by $($file.Name)" }
    }
}
$dependencyReport | Set-Content (Join-Path $ReportDirectory 'NATIVE-DEPENDENCIES.txt')
Get-ChildItem $PublishDirectory -Recurse -File | ForEach-Object {
    [pscustomobject]@{Path=[IO.Path]::GetRelativePath($PublishDirectory,$_.FullName);Bytes=$_.Length;SHA256=(Get-FileHash $_.FullName).Hash}
} | Export-Csv (Join-Path $ReportDirectory 'PUBLISH-MANIFEST.csv') -NoTypeInformation -Encoding utf8
Write-Output 'PASS: .NET/WPF payload, Windows App SDK metadata/runtime, app-local CRT import presence.'
Write-Output 'NOT TESTED: loader behavior without installed shared CRT, desktop notifications and tray.'
