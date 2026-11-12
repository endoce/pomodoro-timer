param([Parameter(Mandatory)][string]$LogicReport)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'portable-validation.ps1')
$LogicReport = (Resolve-Path -LiteralPath $LogicReport).Path
$logic = Get-LogicTestResult $LogicReport
$temporary = Join-Path ([IO.Path]::GetTempPath()) ('pomodoro-validator-tests-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporary | Out-Null
$fixtureCommit = '1111111111111111111111111111111111111111'
$tests = [Collections.Generic.List[object]]::new()
$nativeNames = @('PomodoroTimer.exe','coreclr.dll','clrjit.dll','hostfxr.dll','hostpolicy.dll','wpfgfx_cor3.dll','Microsoft.WindowsAppRuntime.dll',
    'PresentationNative_cor3.dll','msvcp140.dll','vcruntime140.dll','vcruntime140_1.dll')

function Write-FixtureManifest([string]$Payload, [string]$Reports) {
    $null = Assert-PortableReadmeLinks $Payload
    Get-PortableFiles $Payload | Where-Object {
        [IO.Path]::GetRelativePath($Payload, $_.FullName).Replace('\','/') -notin ($PortableEvidenceFiles + 'SHA256SUMS.txt')
    } | ForEach-Object {
        [pscustomobject]@{Path=[IO.Path]::GetRelativePath($Payload, $_.FullName).Replace('\','/');Bytes=$_.Length;
            SHA256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}
    } | Export-Csv -LiteralPath (Join-Path $Reports 'PUBLISH-MANIFEST.csv') -NoTypeInformation
}

function Refresh-Fixture([string]$Root, [switch]$KeepInnerHashes) {
    $reports = Join-Path $Root 'reports'
    $payload = Join-Path $Root $PortableFolderName
    foreach ($name in $PortableEvidenceFiles) {
        Copy-Item -LiteralPath (Join-Path $reports $name) -Destination (Join-Path $payload $name) -Force
    }
    if (!$KeepInnerHashes) {
        $names = @(Get-ChildItem -LiteralPath $payload -Recurse -File -Force | ForEach-Object {
            [IO.Path]::GetRelativePath($payload, $_.FullName).Replace('\','/')
        } | Where-Object { $_ -cne 'SHA256SUMS.txt' })
        # Some negative fixtures intentionally contain empty files; generate their real hashes without blessing them.
        $names | Sort-Object | ForEach-Object {
            "$((Get-FileHash -LiteralPath (Join-Path $payload $_) -Algorithm SHA256).Hash.ToLowerInvariant())  $_"
        } | Set-Content -LiteralPath (Join-Path $payload 'SHA256SUMS.txt')
    }
    $zip = Join-Path $reports $PortableZipName
    if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip }
    [IO.Compression.ZipFile]::CreateFromDirectory($payload, $zip, [IO.Compression.CompressionLevel]::Optimal, $true)
    ($PortableEvidenceFiles + $PortableZipName) | Sort-Object | ForEach-Object {
        "$((Get-FileHash -LiteralPath (Join-Path $reports $_) -Algorithm SHA256).Hash.ToLowerInvariant())  $_"
    } | Set-Content -LiteralPath (Join-Path $reports 'SHA256SUMS.txt')
}

function New-Fixture {
    $root = Join-Path $temporary ([guid]::NewGuid().ToString('N'))
    $payload = Join-Path $root $PortableFolderName
    $reports = Join-Path $root 'reports'
    New-Item -ItemType Directory -Path $payload, $reports | Out-Null
    # Deliberately minimal PE headers: NOT runnable Windows binaries and never a product build.
    foreach ($name in $nativeNames) {
        $bytes = [byte[]]::new(128)
        $bytes[0] = 0x4d; $bytes[1] = 0x5a; $bytes[0x3c] = 64
        $bytes[64] = 0x50; $bytes[65] = 0x45; $bytes[68] = 0x64; $bytes[69] = 0x86
        [Text.Encoding]::UTF8.GetBytes('PomodoroTimerSpike.dll').CopyTo($bytes, 80)
        [IO.File]::WriteAllBytes((Join-Path $payload $name), $bytes)
    }
    foreach ($name in @('PomodoroTimerSpike.dll','PomodoroTimer.Spike.Core.dll','System.Private.CoreLib.dll',
        'PresentationFramework.dll','PresentationCore.dll','WindowsBase.dll',
        'Microsoft.Windows.AppNotifications.winmd')) {
        'SYNTHETIC NON-RUNNABLE VALIDATOR FIXTURE; NOT PRODUCT EVIDENCE' | Set-Content -LiteralPath (Join-Path $payload $name)
    }
    '{"runtimeOptions":{"includedFrameworks":[{"name":"Microsoft.NETCore.App","version":"10.0.14"},{"name":"Microsoft.WindowsDesktop.App","version":"10.0.14"}]}}' |
        Set-Content -LiteralPath (Join-Path $payload 'PomodoroTimerSpike.runtimeconfig.json')
    '{"runtimeTarget":{"name":".NETCoreApp,Version=v10.0/win-x64"}}' | Set-Content -LiteralPath (Join-Path $payload 'PomodoroTimerSpike.deps.json')
    @{Properties=$PortableExpectedProperties} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $reports 'DEPLOYMENT-PROPERTIES.json')
    $null = Copy-PortableUserGuide $payload
    Copy-Item -LiteralPath $LogicReport -Destination (Join-Path $reports 'LOGIC-TESTS.txt')
    @('PASS: .NET/WPF payload, Windows App SDK metadata/runtime, app-local CRT import presence.',
        'NOT TESTED: synthetic report for validators; not a published dependency audit.') | Set-Content -LiteralPath (Join-Path $reports 'PUBLISH-TESTS.txt')
    @('PASS: published WPF XAML initialization and native AppNotificationBuilder',
        'NOT TESTED: synthetic report for validators; no Windows initialization occurred.') | Set-Content -LiteralPath (Join-Path $reports 'INITIALIZATION-TEST.txt')
    Get-PortableTestSummary $reports | Set-Content -LiteralPath (Join-Path $reports 'TEST-SUMMARY.txt')
    'SYNTHETIC Windows SDK report fixture: 10.0.26100.9457; NOT INSTALLED/TESTED' | Set-Content -LiteralPath (Join-Path $reports 'WINDOWS-SDK-INFO.txt')
    'SYNTHETIC native dependency report; NO actual native audit' | Set-Content -LiteralPath (Join-Path $reports 'NATIVE-DEPENDENCIES.txt')
    'SYNTHETIC VC report; NO signature/loader test' | Set-Content -LiteralPath (Join-Path $reports 'VC-RUNTIME.txt')
    @('msvcp140.dll','vcruntime140.dll','vcruntime140_1.dll') | ForEach-Object {
        [pscustomobject]@{File=$_;Version='SYNTHETIC';Source='SYNTHETIC FIXTURE NOT REDIST';
            SHA256=(Get-FileHash -LiteralPath (Join-Path $payload $_) -Algorithm SHA256).Hash}
    } | Export-Csv -LiteralPath (Join-Path $reports 'VC-RUNTIME.csv') -NoTypeInformation
    Write-FixtureManifest $payload $reports
    @"
Commit SHA: $fixtureCommit
.NET SDK: 10.0.403
.NET runtime: 10.0.14
Windows SDK servicing baseline: 10.0.26100.9457
Windows Target Platform Version: 10.0.26100.0
Windows managed reference package: 10.0.26100.57
Windows App SDK: Microsoft.WindowsAppSDK 2.6.5 Stable
Entry point: PomodoroTimer.exe
AssemblyName: PomodoroTimerSpike
TFM: net10.0-windows10.0.26100.0
RID: win-x64
Configuration: Release
.NET SelfContained: true
WindowsAppSDKSelfContained: true
WindowsPackageType: None
Runner: windows-2025-vs2026 (Windows Server 2025; not Windows 11 desktop)
Runner image: SYNTHETIC FIXTURE NOT PRODUCT
Actual build OS: SYNTHETIC FIXTURE NOT WINDOWS
Target desktop: Windows 11 25H2 x64 26200.x, NOT TESTED
Built UTC: $([DateTimeOffset]::UtcNow.ToString('O'))
Windows SDK report SHA256: $((Get-FileHash -LiteralPath (Join-Path $reports 'WINDOWS-SDK-INFO.txt') -Algorithm SHA256).Hash.ToLowerInvariant())
Deployment properties SHA256: $((Get-FileHash -LiteralPath (Join-Path $reports 'DEPLOYMENT-PROPERTIES.json') -Algorithm SHA256).Hash.ToLowerInvariant())
"@ | Set-Content -LiteralPath (Join-Path $reports 'BUILD-INFO.txt')
    Refresh-Fixture $root
    $root
}

function Invoke-FixtureVerification([string]$Root, [switch]$RequireVerificationReport) {
    & (Join-Path $PSScriptRoot 'verify-artifacts.ps1') -ArtifactDirectory (Join-Path $Root 'reports') -ExpectedCommit $fixtureCommit -RequireVerificationReport:$RequireVerificationReport
}

function Add-Rejection([string]$Name, [scriptblock]$Mutation, [string]$ExpectedError) {
    $tests.Add([pscustomobject]@{Name=$Name;Mutation=$Mutation;ExpectedError=$ExpectedError})
}

function Add-ZipEntry([string]$Root, [string]$Name, [switch]$Symlink) {
    $reports = Join-Path $Root 'reports'
    $archive = [IO.Compression.ZipFile]::Open((Join-Path $reports $PortableZipName), [IO.Compression.ZipArchiveMode]::Update)
    try {
        $entry = $archive.CreateEntry($Name)
        if ($Symlink) { $entry.ExternalAttributes = 0xa1ff0000 -as [int] }
        $stream = $entry.Open()
        try { $bytes = [Text.Encoding]::UTF8.GetBytes('SYNTHETIC FIXTURE'); $stream.Write($bytes, 0, $bytes.Length) }
        finally { $stream.Dispose() }
    } finally { $archive.Dispose() }
    Write-FileChecksums $reports ($PortableEvidenceFiles + $PortableZipName) (Join-Path $reports 'SHA256SUMS.txt')
}

Add-Rejection 'empty ZIP' { [IO.File]::WriteAllBytes((Join-Path $reports $PortableZipName), [byte[]]::new(0)) } 'missing or empty'
Add-Rejection 'ZIP archive with no entries' {
    $p=Join-Path $reports $PortableZipName
    Remove-Item -LiteralPath $p
    $a=[IO.Compression.ZipFile]::Open($p,[IO.Compression.ZipArchiveMode]::Create); $a.Dispose()
    Write-FileChecksums $reports ($PortableEvidenceFiles + $PortableZipName) (Join-Path $reports 'SHA256SUMS.txt')
} 'Empty or excessive ZIP entry count'
Add-Rejection 'empty EXE with recomputed hashes' { [IO.File]::WriteAllBytes((Join-Path $payload 'PomodoroTimer.exe'), [byte[]]::new(0)); Refresh-Fixture $root } 'Empty or excessive ZIP file'
Add-Rejection 'missing EXE' { Remove-Item -LiteralPath (Join-Path $payload 'PomodoroTimer.exe'); Refresh-Fixture $root } 'missing or empty.*PomodoroTimer.exe'
Add-Rejection 'missing WPF dependency' { Remove-Item -LiteralPath (Join-Path $payload 'PresentationFramework.dll'); Refresh-Fixture $root } 'missing or empty.*PresentationFramework.dll'
Add-Rejection 'missing .NET dependency' { Remove-Item -LiteralPath (Join-Path $payload 'coreclr.dll'); Refresh-Fixture $root } 'missing or empty.*coreclr.dll'
Add-Rejection 'missing Windows App SDK runtime' { Remove-Item -LiteralPath (Join-Path $payload 'Microsoft.WindowsAppRuntime.dll'); Refresh-Fixture $root } 'missing or empty.*Microsoft.WindowsAppRuntime.dll'
Add-Rejection 'missing Windows App SDK metadata' { Remove-Item -LiteralPath (Join-Path $payload 'Microsoft.Windows.AppNotifications.winmd'); Refresh-Fixture $root } 'runtime/metadata missing'
Add-Rejection 'missing app-local VC DLL' { Remove-Item -LiteralPath (Join-Path $payload 'vcruntime140.dll'); Refresh-Fixture $root } 'missing or empty.*vcruntime140.dll'
Add-Rejection 'x86 apphost' { $p=Join-Path $payload 'PomodoroTimer.exe'; $b=[IO.File]::ReadAllBytes($p); $b[68]=0x4c; $b[69]=1; [IO.File]::WriteAllBytes($p,$b); Refresh-Fixture $root } 'not AMD64'
Add-Rejection 'incorrect embedded apphost DLL' { $p=Join-Path $payload 'PomodoroTimer.exe'; $b=[IO.File]::ReadAllBytes($p); $b[80]=0; [IO.File]::WriteAllBytes($p,$b); Refresh-Fixture $root } 'does not reference'
Add-Rejection 'shared .NET requirement' { '{"runtimeOptions":{"framework":{"name":"Microsoft.NETCore.App","version":"10.0.14"}}}' | Set-Content (Join-Path $payload 'PomodoroTimerSpike.runtimeconfig.json'); Refresh-Fixture $root } 'Shared .NET'
Add-Rejection 'wrong runtime version' { $p=Join-Path $payload 'PomodoroTimerSpike.runtimeconfig.json'; (Get-Content $p -Raw).Replace('10.0.14','10.0.13') | Set-Content $p; Refresh-Fixture $root } 'Runtime version mismatch'
Add-Rejection 'wrong deps RID' { $p=Join-Path $payload 'PomodoroTimerSpike.deps.json'; (Get-Content $p -Raw).Replace('win-x64','win-arm64') | Set-Content $p; Refresh-Fixture $root } 'deps.json is not win-x64'
Add-Rejection 'old unrenamed apphost' { Copy-Item (Join-Path $payload 'PomodoroTimer.exe') (Join-Path $payload 'PomodoroTimerSpike.exe'); Refresh-Fixture $root } 'not renamed'
Add-Rejection 'wrong outer ZIP hash' { $p=Join-Path $reports 'SHA256SUMS.txt'; (Get-Content $p) -replace "^[0-9a-f]{64}(  $([regex]::Escape($PortableZipName)))$",(('0'*64)+'$1') | Set-Content $p } 'SHA256 mismatch'
Add-Rejection 'wrong inner hash' { Add-Content (Join-Path $payload 'System.Private.CoreLib.dll') 'changed'; Refresh-Fixture $root -KeepInnerHashes } 'manifest mismatch|SHA256 mismatch'
Add-Rejection 'incomplete checksum coverage' { $p=Join-Path $payload 'SHA256SUMS.txt'; @(Get-Content $p) | Select-Object -Skip 1 | Set-Content $p; Refresh-Fixture $root -KeepInnerHashes } 'coverage incomplete'
Add-Rejection 'wrong commit' { $p=Join-Path $reports 'BUILD-INFO.txt'; (Get-Content $p -Raw).TrimEnd().Replace($fixtureCommit,('2'*40)) | Set-Content $p; Refresh-Fixture $root } 'BUILD-INFO mismatch: Commit SHA'
Add-Rejection 'wrong SDK identity' { $p=Join-Path $reports 'BUILD-INFO.txt'; (Get-Content $p -Raw).TrimEnd().Replace('10.0.403','10.0.402') | Set-Content $p; Refresh-Fixture $root } 'BUILD-INFO mismatch: .NET SDK'
Add-Rejection 'wrong deployment property' { $p=Join-Path $reports 'DEPLOYMENT-PROPERTIES.json'; (Get-Content $p -Raw).Replace('win-x64','win-arm64') | Set-Content $p; Refresh-Fixture $root } 'Deployment property mismatch'
Add-Rejection 'SDK report tampering' { Add-Content (Join-Path $reports 'WINDOWS-SDK-INFO.txt') 'changed'; Refresh-Fixture $root } 'evidence hash mismatch'
Add-Rejection 'empty raw logic report' { [IO.File]::WriteAllBytes((Join-Path $reports 'LOGIC-TESTS.txt'),[byte[]]::new(0)); Refresh-Fixture $root } 'missing or empty'
Add-Rejection 'fabricated totals without individual results' { 'TOTAL 84; PASSED 84; FAILED 0; SKIPPED 0' | Set-Content (Join-Path $reports 'LOGIC-TESTS.txt'); Refresh-Fixture $root } 'inconsistent or unsuccessful'
Add-Rejection 'duplicate individual results' {
    @('PASS duplicated','PASS duplicated','TOTAL 2; PASSED 2; FAILED 0; SKIPPED 0','NOT TESTED: fixture') |
        Set-Content (Join-Path $reports 'LOGIC-TESTS.txt'); Refresh-Fixture $root
} 'inconsistent or unsuccessful'
Add-Rejection 'failed actual result' {
    @('PASS one','FAIL two: synthetic failure','TOTAL 2; PASSED 1; FAILED 1; SKIPPED 0','NOT TESTED: fixture') |
        Set-Content (Join-Path $reports 'LOGIC-TESTS.txt'); Refresh-Fixture $root
} 'inconsistent or unsuccessful'
Add-Rejection 'skipped actual result' {
    @('PASS one','SKIP two: synthetic skip','TOTAL 2; PASSED 1; FAILED 0; SKIPPED 1','NOT TESTED: fixture') |
        Set-Content (Join-Path $reports 'LOGIC-TESTS.txt'); Refresh-Fixture $root
} 'inconsistent or unsuccessful'
Add-Rejection 'fabricated success summary' { '84 tests passed' | Set-Content (Join-Path $reports 'TEST-SUMMARY.txt'); Refresh-Fixture $root } 'does not match'
Add-Rejection 'lost NOT TESTED evidence' { $p=Join-Path $reports 'INITIALIZATION-TEST.txt'; Get-Content $p | Where-Object { $_ -notmatch '^NOT TESTED' } | Set-Content (Join-Path $reports 'changed.txt'); Move-Item (Join-Path $reports 'changed.txt') $p -Force; Refresh-Fixture $root } 'inconsistent execution evidence'
Add-Rejection 'empty initialization success placeholder' { 'PASS' | Set-Content (Join-Path $reports 'INITIALIZATION-TEST.txt'); Refresh-Fixture $root } 'inconsistent execution evidence'
Add-Rejection 'Setup payload' { Add-ZipEntry $root 'PomodoroTimer/Setup.exe' } 'Forbidden Portable'
Add-Rejection 'Inno source' { Add-ZipEntry $root 'PomodoroTimer/spike.iss' } 'Forbidden Portable'
Add-Rejection 'source code' { Add-ZipEntry $root 'PomodoroTimer/Program.cs' } 'Forbidden Portable'
Add-Rejection 'test executable' { Add-ZipEntry $root 'PomodoroTimer/PomodoroTimer.Spike.Tests.exe' } 'Forbidden Portable'
Add-Rejection 'temporary obj folder' { Add-ZipEntry $root 'PomodoroTimer/obj/cache.txt' } 'Forbidden Portable'
Add-Rejection 'user data' { Add-ZipEntry $root 'PomodoroTimer/Data/settings.json' } 'Forbidden Portable'
Add-Rejection 'debug symbols' { Add-ZipEntry $root 'PomodoroTimer/app.pdb' } 'Forbidden Portable'
Add-Rejection 'wrong-platform runtime' { Add-ZipEntry $root 'PomodoroTimer/runtimes/win-arm64/native/runtime.dll' } 'Forbidden Portable'
Add-Rejection 'path traversal' { Add-ZipEntry $root 'PomodoroTimer/../escape.txt' } 'Unsafe path'
Add-Rejection 'absolute path' { Add-ZipEntry $root '/escape.txt' } 'Unsafe path'
Add-Rejection 'backslash path' { Add-ZipEntry $root 'PomodoroTimer\escape.txt' } 'Unsafe path'
Add-Rejection 'alternate data stream' { Add-ZipEntry $root 'PomodoroTimer/file.txt:stream' } 'Unsafe path'
Add-Rejection 'reserved Windows device' { Add-ZipEntry $root 'PomodoroTimer/NUL.txt' } 'Unsafe path segment'
Add-Rejection 'wrong ZIP root' { Add-ZipEntry $root 'Other/file.txt' } 'Unexpected ZIP root'
Add-Rejection 'duplicate case-insensitive entry' { Add-ZipEntry $root 'PomodoroTimer/pomodorotimer.EXE' } 'Duplicate ZIP entry'
Add-Rejection 'symbolic link entry' { Add-ZipEntry $root 'PomodoroTimer/link.txt' -Symlink } 'link/reparse entry'
Add-Rejection 'file/directory conflict' { Add-ZipEntry $root 'PomodoroTimer/nested'; Add-ZipEntry $root 'PomodoroTimer/nested/file.txt' } 'file/directory conflict'
Add-Rejection 'extra ZIP artifact' { Copy-Item (Join-Path $reports $PortableZipName) (Join-Path $reports 'other.zip') } 'Only the single'
Add-Rejection 'extra installer artifact' { 'synthetic installer negative fixture' | Set-Content (Join-Path $reports 'Setup.exe') } 'Only the single'
Add-Rejection 'README missing from ZIP' { Remove-Item (Join-Path $payload 'README.md'); Refresh-Fixture $root } 'missing or empty.*README.md'
Add-Rejection 'README broken repository-relative link with recomputed hashes' {
    '[developer document](docs/STATUS.md)' | Set-Content (Join-Path $payload 'README.md')
    Refresh-Fixture $root
} 'missing or empty.*STATUS.md'

# Independently specified fixtures: no production host override, environment switch or relaxed skip policy.
$unixData = 'Actual read-only Data directory rejects saves without changing data'
$unixLogs = 'Actual read-only Logs directory warns without blocking initialization'
$skipReason = 'Unix permissions test; Windows ACL validation is separate'
$linuxRecords = @("PASS $unixData", "PASS $unixLogs", 'PASS ordinary fixture test')
$windowsRecords = @("SKIP ${unixData}: $skipReason", "SKIP ${unixLogs}: $skipReason", 'PASS ordinary fixture test')
function New-ParserLines([string[]]$Records, [string]$Totals) {
    $Records
    $Totals
    'NOT TESTED: synthetic parser fixture, not executed application tests.'
    'NOT TESTED: Windows ACL validation is separate; Unix permission results do not validate Windows ACLs.'
}
$parserTests = [Collections.Generic.List[object]]::new()
function Add-ParserCase([string]$Name, [string]$Platform, [string[]]$Lines, [string]$Error = '') {
    $parserTests.Add([pscustomobject]@{Name=$Name;Platform=$Platform;Lines=$Lines;Error=$Error})
}
$linuxLines = @(New-ParserLines $linuxRecords 'TOTAL 3; PASSED 3; FAILED 0; SKIPPED 0')
$windowsLines = @(New-ParserLines $windowsRecords 'TOTAL 3; PASSED 1; FAILED 0; SKIPPED 2')
Add-ParserCase 'Linux both Unix permission tests PASS' Linux $linuxLines
Add-ParserCase 'Windows exact two permission SKIPs' Windows $windowsLines
Add-ParserCase 'Linux permission SKIP forbidden' Linux $windowsLines 'Linux SKIP is forbidden'
Add-ParserCase 'Linux unrelated SKIP forbidden' Linux (New-ParserLines ($linuxRecords[0..1] + 'SKIP ordinary fixture test: arbitrary') 'TOTAL 3; PASSED 2; FAILED 0; SKIPPED 1') 'Linux SKIP is forbidden'
Add-ParserCase 'Windows third SKIP forbidden' Windows (New-ParserLines ($windowsRecords[0..1] + 'SKIP ordinary fixture test: arbitrary') 'TOTAL 3; PASSED 0; FAILED 0; SKIPPED 3') 'exactly two'
Add-ParserCase 'Windows wrong SKIP name' Windows ($windowsLines -creplace [regex]::Escape($unixData), 'wrong Data test') 'unauthorized Windows SKIP'
Add-ParserCase 'Windows wrong SKIP reason' Windows ($windowsLines -creplace 'validation is separate', 'validation not needed') 'unauthorized Windows SKIP'
Add-ParserCase 'Windows case-changed SKIP name' Windows ($windowsLines -creplace 'Actual read-only Data', 'actual read-only Data') 'unauthorized Windows SKIP'
Add-ParserCase 'Windows case-changed SKIP reason' Windows ($windowsLines -creplace 'Unix permissions', 'unix permissions') 'unauthorized Windows SKIP'
Add-ParserCase 'Windows only one SKIP' Windows (New-ParserLines @($windowsRecords[0], "PASS $unixLogs", $windowsRecords[2]) 'TOTAL 3; PASSED 2; FAILED 0; SKIPPED 1') 'exactly two'
Add-ParserCase 'Windows zero SKIPs even when permissions PASS' Windows $linuxLines 'exactly two'
Add-ParserCase 'Linux any FAIL forbidden' Linux (New-ParserLines ($linuxRecords[0..1] + 'FAIL ordinary fixture test: synthetic failure') 'TOTAL 3; PASSED 2; FAILED 1; SKIPPED 0') 'counts, missing results or failures'
Add-ParserCase 'Windows any FAIL forbidden' Windows (New-ParserLines ($windowsRecords[0..1] + 'FAIL ordinary fixture test: synthetic failure') 'TOTAL 3; PASSED 0; FAILED 1; SKIPPED 2') 'counts, missing results or failures'
Add-ParserCase 'TOTAL differs from actual records' Linux ($linuxLines -creplace 'TOTAL 3; PASSED 3', 'TOTAL 4; PASSED 4') 'counts, missing results or failures'
Add-ParserCase 'duplicate PASS name' Linux (New-ParserLines ($linuxRecords + $linuxRecords[0]) 'TOTAL 4; PASSED 4; FAILED 0; SKIPPED 0') 'duplicate result'
Add-ParserCase 'duplicate name across PASS and SKIP' Windows (New-ParserLines ($windowsRecords + "PASS $unixData") 'TOTAL 4; PASSED 2; FAILED 0; SKIPPED 2') 'duplicate result'
Add-ParserCase 'missing individual result' Linux ($linuxLines | Where-Object { $_ -cne $linuxRecords[2] }) 'counts, missing results or failures'
Add-ParserCase 'missing Linux permission result with adjusted totals' Linux (New-ParserLines @($linuxRecords[0], $linuxRecords[2]) 'TOTAL 2; PASSED 2; FAILED 0; SKIPPED 0') 'missing Linux permission PASS'
Add-ParserCase 'unrecognized result status' Linux ($linuxLines -creplace '^PASS ordinary', 'OK ordinary') 'unrecognized line'
Add-ParserCase 'unknown diagnostic line cannot hide a result' Linux ($linuxLines + 'unexpected output') 'unrecognized line'
Add-ParserCase 'PASS without a test name' Linux ($linuxLines -creplace '^PASS ordinary fixture test$', 'PASS') 'unrecognized line'
Add-ParserCase 'SKIP without a reason' Windows ($windowsLines -creplace [regex]::Escape(": $skipReason"), '') 'unrecognized line'
Add-ParserCase 'summary counters differ from record statuses' Windows ($windowsLines -creplace 'PASSED 1; FAILED 0; SKIPPED 2', 'PASSED 2; FAILED 0; SKIPPED 1') 'counts, missing results or failures'
Add-ParserCase 'TOTAL arithmetic mismatch' Linux ($linuxLines -creplace 'TOTAL 3;', 'TOTAL 4;') 'counts, missing results or failures'
Add-ParserCase 'missing TOTAL' Linux ($linuxLines | Where-Object { $_ -notmatch '^TOTAL ' }) 'missing totals'
Add-ParserCase 'duplicate TOTAL' Linux ($linuxLines + 'TOTAL 3; PASSED 3; FAILED 0; SKIPPED 0') 'duplicate totals'
Add-ParserCase 'malformed TOTAL' Linux ($linuxLines -creplace 'TOTAL 3;', 'TOTAL three;') 'unrecognized line'
Add-ParserCase 'zero tests' Linux (New-ParserLines @() 'TOTAL 0; PASSED 0; FAILED 0; SKIPPED 0') 'counts, missing results or failures'
Add-ParserCase 'missing NOT TESTED evidence' Linux ($linuxLines | Where-Object { $_ -notmatch '^NOT TESTED:' }) 'counts, missing results or failures'
Add-ParserCase 'result after TOTAL' Linux ($linuxLines + 'PASS late test') 'late or duplicate result'
Add-ParserCase 'lowercase status is unrecognized' Linux ($linuxLines -creplace '^PASS ordinary', 'pass ordinary') 'unrecognized line'
Add-ParserCase 'whitespace-altered SKIP reason' Windows ($windowsLines -creplace 'validation is separate', 'validation  is separate') 'unauthorized Windows SKIP'
Add-ParserCase 'protected stderr diagnostic format' Linux (@('2026-11-12T00:00:00.0000000+00:00 Warning logs.write.failed: IOException; diagnostic sink unavailable.') + $linuxLines)
Add-ParserCase 'malformed stderr diagnostic cannot hide result' Linux (@('2026-11-12T00:00:00.0000000+00:00 Warning logs.write.failed: PASS hidden; diagnostic sink unavailable.') + $linuxLines) 'unrecognized line'
Add-ParserCase 'invalid diagnostic timestamp' Linux (@('2026-99-12T00:00:00.0000000+00:00 Warning logs.write.failed: IOException; diagnostic sink unavailable.') + $linuxLines) 'invalid diagnostic'

$linkTests = @(
    @{Name='standalone guide';Text='# Standalone guide';Relative=0;External=0},
    @{Name='existing relative inline link';Text='[help](docs/help.md)';Relative=1;External=0},
    @{Name='relative image';Text='![help](docs/help.md)';Relative=1;External=0},
    @{Name='balanced parentheses in destination';Text='[help](docs/help(1).md "title")';Relative=1;External=0},
    @{Name='angle destination with spaces';Text='[help](<docs/help file.md>)';Relative=1;External=0},
    @{Name='percent-encoded relative path';Text='[help](docs/help%20file.md#section)';Relative=1;External=0},
    @{Name='same README fragment';Text='[section](#section)';Relative=1;External=0},
    @{Name='dot relative path';Text='[help](./docs/help.md)';Relative=1;External=0},
    @{Name='reference links';Text="[help][manual]`n[manual]: docs/help.md";Relative=1;External=0},
    @{Name='collapsed reference';Text="[manual][]`n[manual]: docs/help.md";Relative=1;External=0},
    @{Name='shortcut reference';Text="[manual]`n[manual]: docs/help.md";Relative=1;External=0},
    @{Name='external HTTP HTTPS separate';Text='[HTTP](http://example.com/help) [HTTPS](https://example.com/help) <https://example.com/other>';Relative=0;External=3},
    @{Name='missing repository document';Text='[status](docs/STATUS.md)';Error='missing or empty'},
    @{Name='missing reference definition';Text='[help][missing]';Error='Missing Portable README link definition'},
    @{Name='directory is not a linked file';Text='[help](docs/)';Error='missing or empty'},
    @{Name='escape ZIP root';Text='[help](../outside.md)';Error='Unsafe path'},
    @{Name='encoded escape ZIP root';Text='[help](%2e%2e/outside.md)';Error='Unsafe path'},
    @{Name='absolute file path';Text='[help](/etc/hosts)';Error='Unsafe Portable README link'},
    @{Name='non HTTP URI';Text='[help](file:///etc/hosts)';Error='Unsafe Portable README link'},
    @{Name='backslash path';Text='[help](docs\help.md)';Error='Unsafe Portable README link'},
    @{Name='wrong package casing';Text='[help](docs/HELP.md)';Error='missing or empty|not a packaged file'},
    @{Name='unsupported malformed link';Text='[help](docs/help.md';Error='Unsupported Portable README link syntax'},
    @{Name='unsupported multiline label fails closed';Text="[help`nlabel](missing.md)";Error='Unsupported Portable README link syntax'},
    @{Name='unsupported nested label fails closed';Text='[help [label]](missing.md)';Error='Unsupported Portable README link syntax'},
    @{Name='HTML link fails closed';Text='<a href="missing.md">help</a>';Error='Unsupported Portable README link syntax'},
    @{Name='percent-encoded backslash';Text='[help](docs%5chelp.md)';Error='Unsafe Portable README link'},
    @{Name='empty link target file';Text='[help](empty.md)';Error='missing or empty'}
)

[int]$passed = 0
try {
    'SYNTHETIC VALIDATOR TESTS ONLY; minimal fake PE fixtures are not runnable product ZIPs, Windows builds or desktop evidence.'
    $root = New-Fixture
    $baseline = @(Invoke-FixtureVerification $root)
    if ($baseline.Count -ne 2 -or $baseline[0] -notmatch '^PASS:' -or $baseline[1] -notmatch '^NOT TESTED:') { throw 'Positive validator fixture did not complete.' }
    $passed++; 'PASS validator: consistent non-runnable structural fixture'
    $guideSource = Join-Path $PSScriptRoot '../docs/PORTABLE-USER-GUIDE.md'
    if ((Get-FileHash $guideSource).Hash -cne (Get-FileHash (Join-Path $root "$PortableFolderName/README.md")).Hash) { throw 'Fixture did not copy the exact user guide.' }
    $passed++; 'PASS validator: exact standalone user guide copied as README before manifest'
    $reports = Join-Path $root 'reports'
    $baseline | Set-Content -LiteralPath (Join-Path $reports 'ARTIFACT-TESTS.txt')
    @('SYNTHETIC VALIDATOR TESTS ONLY; report-parser fixture, not product evidence.',
        'PASS validator: synthetic report parser fixture','VALIDATOR TOTAL 1; PASSED 1; FAILED 0; SKIPPED 0',
        "Real input logic report: TOTAL $($logic.Total); PASSED $($logic.Passed); FAILED $($logic.Failed); SKIPPED $($logic.Skipped); SHA256 $((Get-FileHash -LiteralPath $LogicReport -Algorithm SHA256).Hash.ToLowerInvariant())",
        'NOT TESTED: synthetic report parser input only') | Set-Content -LiteralPath (Join-Path $reports 'PIPELINE-SCRIPT-TESTS.txt')
    Write-FileChecksums $reports ($PortableEvidenceFiles + $PortableZipName + 'ARTIFACT-TESTS.txt' + 'PIPELINE-SCRIPT-TESTS.txt') (Join-Path $reports 'SHA256SUMS.txt')
    $null = Invoke-FixtureVerification $root -RequireVerificationReport
    $passed++; 'PASS validator: finalized external report checksums without hash cycle'
    'PASS' | Set-Content -LiteralPath (Join-Path $reports 'PIPELINE-SCRIPT-TESTS.txt')
    Write-FileChecksums $reports ($PortableEvidenceFiles + $PortableZipName + 'ARTIFACT-TESTS.txt' + 'PIPELINE-SCRIPT-TESTS.txt') (Join-Path $reports 'SHA256SUMS.txt')
    $rejected = $false
    try { $null = Invoke-FixtureVerification $root -RequireVerificationReport }
    catch {
        if ($_.Exception.Message -notmatch 'validator test report totals') { throw }
        $rejected = $true
    }
    if (!$rejected) { throw 'Validator accepted fabricated validator success report.' }
    $passed++; 'PASS validator rejection: fabricated validator success report'
    foreach ($test in $tests) {
        $root = New-Fixture
        $reports = Join-Path $root 'reports'
        $payload = Join-Path $root $PortableFolderName
        & $test.Mutation
        $rejected = $false
        try { $null = Invoke-FixtureVerification $root }
        catch {
            if ($_.Exception.Message -notmatch $test.ExpectedError) { throw "Wrong rejection for $($test.Name): $($_.Exception.Message)" }
            $rejected = $true
        }
        if (!$rejected) { throw "Validator accepted negative fixture: $($test.Name)" }
        $passed++; "PASS validator rejection: $($test.Name)"
    }
    # Verify the parser derives counts rather than requiring a hard-coded 84.
    $small = Join-Path $temporary 'dynamic-count.txt'
    @('PASS structural parser case one','PASS structural parser case two',"PASS $unixData", "PASS $unixLogs",'TOTAL 4; PASSED 4; FAILED 0; SKIPPED 0',
        'NOT TESTED: structural parser input, not application execution') | Set-Content -LiteralPath $small
    if ((ConvertFrom-LogicTestOutput @(Get-Content $small) Linux).Total -ne 4) { throw 'Test count was not derived from actual report structure.' }
    $passed++; 'PASS validator: dynamic report count'
    foreach ($test in $parserTests) {
        $rejected = $false
        try {
            $parsed = ConvertFrom-LogicTestOutput $test.Lines $test.Platform
            if ($test.Error) { throw "Parser accepted negative fixture: $($test.Name)" }
            if ($test.Platform -ceq 'Windows' -and ($parsed.Passed -ne 1 -or $parsed.Skipped -ne 2 -or
                ($parsed.SkipRecords.Raw -join "`n") -cne ($windowsRecords[0..1] -join "`n"))) { throw 'Windows SKIPs were not preserved.' }
        } catch {
            if (!$test.Error -or $_.Exception.Message -notmatch $test.Error) { throw }
            $rejected = $true
        }
        if ([bool]$test.Error -ne $rejected) { throw "Unexpected parser outcome: $($test.Name)" }
        $passed++; "PASS validator: parser $($test.Name)"
    }
    # Simulate the complete actual report with only the two platform-dependent outcomes changed.
    $simulatedWindows = @(Get-Content $LogicReport | ForEach-Object {
        if ($_ -ceq "PASS $unixData") { "SKIP ${unixData}: $skipReason" }
        elseif ($_ -ceq "PASS $unixLogs") { "SKIP ${unixLogs}: $skipReason" }
        elseif ($_ -match '^TOTAL ') { "TOTAL $($logic.Total); PASSED $($logic.Total - 2); FAILED 0; SKIPPED 2" }
        else { $_ }
    })
    $windows = ConvertFrom-LogicTestOutput $simulatedWindows Windows
    if ($windows.Total -ne $logic.Total -or $windows.Passed -ne $logic.Total - 2 -or $windows.SkipRecords.Count -ne 2) { throw 'Full simulated Windows output was not preserved.' }
    $passed++; 'PASS validator: full real-report Windows output simulation (not Windows execution)'
    $renderRoot = New-Fixture
    $renderReports = Join-Path $renderRoot 'reports'
    $simulatedWindows | Set-Content (Join-Path $renderReports 'LOGIC-TESTS.txt')
    $rendered = @(Format-PortableTestSummary $renderReports $windows)
    $expectedCounts = "Executed logic tests: TOTAL $($windows.Total); PASSED $($windows.Passed); FAILED 0; SKIPPED 2"
    if (@($rendered | Where-Object { $_ -ceq $expectedCounts }).Count -ne 1 -or
        @($rendered | Where-Object { $_ -cmatch '^NOT TESTED: Windows ACL ' }).Count -eq 0) { throw 'Windows summary lost counts or ACL limitation.' }
    foreach ($skip in $windows.SkipRecords) {
        if (@($rendered | Where-Object { $_ -ceq $skip.Raw }).Count -ne 1 -or
            @($rendered | Where-Object { $_ -ceq "PASS $($skip.Name)" }).Count -ne 0) { throw 'Windows summary changed or lost an original SKIP.' }
    }
    $passed++; 'PASS validator: simulated Windows summary preserves counts raw SKIPs reasons and ACL NOT TESTED'
    foreach ($test in $linkTests) {
        $linkRoot = Join-Path $temporary ([guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path (Join-Path $linkRoot 'docs') | Out-Null
        foreach ($name in @('help.md','help(1).md','help file.md')) { 'fixture help' | Set-Content (Join-Path $linkRoot "docs/$name") }
        if ($test.Name -ceq 'empty link target file') { [IO.File]::WriteAllBytes((Join-Path $linkRoot 'empty.md'), [byte[]]::new(0)) }
        $test.Text | Set-Content (Join-Path $linkRoot 'README.md')
        $rejected = $false
        try {
            $links = Assert-PortableReadmeLinks $linkRoot
            if ($test.ContainsKey('Error')) { throw "Links accepted negative fixture: $($test.Name)" }
            if ($links.RelativeLinks -ne $test.Relative -or $links.ExternalHttpLinks -ne $test.External) { throw "Incorrect link counts: $($test.Name)" }
        } catch {
            if (!$test.ContainsKey('Error') -or $_.Exception.Message -notmatch $test.Error) { throw }
            $rejected = $true
        }
        if ($test.ContainsKey('Error') -ne $rejected) { throw "Unexpected link outcome: $($test.Name)" }
        $passed++; "PASS validator: README links $($test.Name)"
    }
    $manifestRoot = Join-Path $temporary 'broken-link-manifest'
    New-Item -ItemType Directory -Path $manifestRoot | Out-Null
    '[missing](docs/absent.md)' | Set-Content (Join-Path $manifestRoot 'README.md')
    $manifest = Join-Path $temporary 'must-not-be-generated.csv'
    $rejected = $false
    try { Write-PublishManifest $manifestRoot $manifest }
    catch { if ($_.Exception.Message -notmatch 'missing or empty') { throw }; $rejected = $true }
    if (!$rejected -or (Test-Path -LiteralPath $manifest)) { throw 'Manifest was generated despite a broken README link.' }
    $passed++; 'PASS validator: broken README link rejected before manifest or manifest hashes exist'
    "VALIDATOR TOTAL $passed; PASSED $passed; FAILED 0; SKIPPED 0"
    "Real input logic report: TOTAL $($logic.Total); PASSED $($logic.Passed); FAILED $($logic.Failed); SKIPPED $($logic.Skipped); SHA256 $((Get-FileHash -LiteralPath $LogicReport -Algorithm SHA256).Hash.ToLowerInvariant())"
    'NOT TESTED: real Portable ZIP generation, native Windows loader/WPF initialization, Windows Runner and Windows 11 25H2 desktop.'
} finally {
    Remove-Item -LiteralPath $temporary -Recurse -Force
}
