# Shared, platform-neutral file/report checks. These do not execute WPF or authenticate a publisher.
Set-StrictMode -Version Latest

$PortableZipName = 'PomodoroTimer-Portable-Windows-x64.zip'
$PortableFolderName = 'PomodoroTimer'
$PortableEvidenceFiles = @('BUILD-INFO.txt','TEST-SUMMARY.txt','LOGIC-TESTS.txt','PUBLISH-TESTS.txt',
    'INITIALIZATION-TEST.txt','DEPLOYMENT-PROPERTIES.json','PUBLISH-MANIFEST.csv',
    'NATIVE-DEPENDENCIES.txt','VC-RUNTIME.csv','VC-RUNTIME.txt','WINDOWS-SDK-INFO.txt')
$PortableExpectedProperties = [ordered]@{
    TargetFramework='net10.0-windows10.0.26100.0'; RuntimeIdentifier='win-x64'; Configuration='Release'
    SelfContained='true'; WindowsAppSDKSelfContained='true'; WindowsPackageType='None'
    PublishSingleFile='false'; PublishTrimmed='false'; AssemblyName='PomodoroTimerSpike'
}

function Assert-NonemptyFile([string]$Path) {
    if (!(Test-Path -LiteralPath $Path -PathType Leaf) -or (Get-Item -LiteralPath $Path).Length -eq 0) {
        throw "Required file missing or empty: $Path"
    }
}

function Assert-SafeRelativePath([string]$Path) {
    if (!$Path -or $Path.Contains('\') -or $Path.Contains(':') -or $Path.Contains('//') -or $Path.StartsWith('/') -or
        $Path -match '[\x00-\x1f]' -or $Path -match '(?i)^(?:[a-z]:|//)') { throw "Unsafe path: $Path" }
    foreach ($segment in $Path.TrimEnd('/').Split('/')) {
        if (!$segment -or $segment -in @('.','..') -or $segment -match '[. ]$' -or
            $segment -match '[<>"|?*]' -or $segment -match '(?i)^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)') {
            throw "Unsafe path segment: $Path"
        }
    }
}

function Assert-PortableFileName([string]$Path) {
    Assert-SafeRelativePath $Path
    if ($Path -match '(?i)(^|/)(App|Core|bin|obj|tests?|tools|installer|artifacts|stage|\.git|\.github|Data|Logs)(/|$)' -or
        $Path -match '(?i)(setup[^/]*\.exe|iscc\.exe|testhost[^/]*|[^/]*\.tests(?:\.[^/]+)?|[^/]*\.vshost\.exe)$' -or
        $Path -match '(?i)(^|/)(innosetup[^/]*|vc_redist[^/]*|nuget|dotnet|csc|vbc|ildasm|dumpbin|vswhere|git)\.exe$' -or
        $Path -match '(?i)\.(cs|vb|fs|csx|csproj|vbproj|fsproj|sln|slnx|props|targets|iss|isl|pdb|tmp|log|zip|nupkg|ps1|bat|cmd|sh|yml|yaml)$' -or
        $Path -match '(?i)(^|/)(runtimes|runtimes-framework)/(linux[^/]*|osx[^/]*|win-x86|win-arm64(?:ec)?)(/|$)') {
        throw "Forbidden Portable payload: $Path"
    }
}

function Get-PortableFiles([string]$Directory) {
    foreach ($entry in Get-ChildItem -LiteralPath $Directory -Force -Recurse) {
        if ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Payload link not supported: $($entry.FullName)" }
    }
    foreach ($file in Get-ChildItem -LiteralPath $Directory -Force -Recurse -File) {
        $relative = [IO.Path]::GetRelativePath($Directory, $file.FullName).Replace('\','/')
        # Initialization may create application-local diagnostics; never distribute user data or debug symbols.
        if ($relative -match '^(?i)(Data|Logs)/' -or $file.Extension -ieq '.pdb') { continue }
        Assert-PortableFileName $relative
        Assert-NonemptyFile $file.FullName
        if ($file.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Payload link not supported: $relative" }
        $file
    }
}

function Assert-Amd64PE([string]$Path) {
    Assert-NonemptyFile $Path
    $bytes = [IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -lt 64 -or $bytes[0] -ne 0x4d -or $bytes[1] -ne 0x5a) { throw "Invalid PE: $Path" }
    $offset = [BitConverter]::ToInt32($bytes, 0x3c)
    if ($offset -lt 64 -or $offset -gt $bytes.Length - 6 -or
        [BitConverter]::ToUInt32($bytes, $offset) -ne 0x00004550 -or
        [BitConverter]::ToUInt16($bytes, $offset + 4) -ne 0x8664) { throw "PE is not AMD64: $Path" }
}

function Assert-PortablePayload([string]$Directory) {
    $required = @('PomodoroTimer.exe','PomodoroTimerSpike.dll','PomodoroTimer.Spike.Core.dll',
        'PomodoroTimerSpike.runtimeconfig.json','PomodoroTimerSpike.deps.json',
        'coreclr.dll','clrjit.dll','hostfxr.dll','hostpolicy.dll','System.Private.CoreLib.dll','Microsoft.WindowsAppRuntime.dll',
        'PresentationFramework.dll','PresentationCore.dll','WindowsBase.dll',
        'wpfgfx_cor3.dll','PresentationNative_cor3.dll','msvcp140.dll','vcruntime140.dll','vcruntime140_1.dll')
    foreach ($name in $required) { Assert-NonemptyFile (Join-Path $Directory $name) }
    if (Test-Path -LiteralPath (Join-Path $Directory 'PomodoroTimerSpike.exe')) { throw 'Old apphost was not renamed.' }
    foreach ($name in @('PomodoroTimer.exe','coreclr.dll','clrjit.dll','hostfxr.dll','hostpolicy.dll','Microsoft.WindowsAppRuntime.dll',
        'wpfgfx_cor3.dll','PresentationNative_cor3.dll','msvcp140.dll','vcruntime140.dll','vcruntime140_1.dll')) {
        Assert-Amd64PE (Join-Path $Directory $name)
    }
    $hostText = [Text.Encoding]::UTF8.GetString([IO.File]::ReadAllBytes((Join-Path $Directory 'PomodoroTimer.exe')))
    if (!$hostText.Contains('PomodoroTimerSpike.dll')) { throw 'Renamed apphost does not reference the preserved application DLL.' }
    $files = @(Get-PortableFiles $Directory)
    if (@($files | Where-Object Name -like '*WindowsAppRuntime*.dll').Count -eq 0 -or
        @($files | Where-Object Extension -eq '.winmd').Count -eq 0) { throw 'Windows App SDK runtime/metadata missing.' }
    $config = Get-Content -LiteralPath (Join-Path $Directory 'PomodoroTimerSpike.runtimeconfig.json') -Raw | ConvertFrom-Json
    if ($config.runtimeOptions.PSObject.Properties.Name -contains 'framework' -or
        $config.runtimeOptions.PSObject.Properties.Name -contains 'frameworks') { throw 'Shared .NET framework dependency found.' }
    foreach ($name in @('Microsoft.NETCore.App','Microsoft.WindowsDesktop.App')) {
        $framework = @($config.runtimeOptions.includedFrameworks | Where-Object name -eq $name)
        if ($framework.Count -ne 1 -or $framework[0].version -ne '10.0.14') { throw "Runtime version mismatch: $name" }
    }
    $deps = Get-Content -LiteralPath (Join-Path $Directory 'PomodoroTimerSpike.deps.json') -Raw | ConvertFrom-Json
    if ($deps.runtimeTarget.name -notmatch '/win-x64$') { throw 'deps.json is not win-x64.' }
}

function Assert-DeploymentProperties([string]$Path) {
    Assert-NonemptyFile $Path
    $evaluated = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    foreach ($key in $PortableExpectedProperties.Keys) {
        if ([string]$evaluated.Properties.$key -cne $PortableExpectedProperties[$key]) { throw "Deployment property mismatch: $key" }
    }
}

function Get-UnixPermissionTestNames {
    # Exact protected test registrations in Tests/PortableTests.cs; not an extensible skip policy.
    'Actual read-only Data directory rejects saves without changing data'
    'Actual read-only Logs directory warns without blocking initialization'
}

function ConvertFrom-LogicTestOutput([string[]]$Lines, [ValidateSet('Linux','Windows')][string]$Platform) {
    # Pure parser: explicit platforms are for synthetic parser fixtures only. Release callers use
    # Get-LogicTestResult, whose platform is derived from the OS and cannot be overridden.
    $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $records = [Collections.Generic.List[object]]::new()
    $totals = $null
    $notTested = 0
    $diagnostics = 0
    foreach ($line in $Lines) {
        if ($line -ceq '') { continue }
        if ($line -match '[\x00-\x1f]') { throw 'Executed-test output is inconsistent or unsuccessful: control character.' }
        if ($line -cmatch '^TOTAL (\d+); PASSED (\d+); FAILED (\d+); SKIPPED (\d+)$') {
            if ($null -ne $totals) { throw 'Executed-test output is inconsistent or unsuccessful: duplicate totals.' }
            $totals = @([int]$Matches[1], [int]$Matches[2], [int]$Matches[3], [int]$Matches[4])
            continue
        }
        if ($line -cmatch '^NOT TESTED: \S.*$') { $notTested++; continue }
        # DiagnosticReporter.Fallback writes this exact structured line to stderr during protected tests.
        # Do not accept arbitrary diagnostics or mistake them for unrecognized test result records.
        if ($line -cmatch '^(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{7}\+00:00) Warning [a-z][a-z0-9]*(?:\.[a-z][a-z0-9]*)+: [A-Za-z_][A-Za-z0-9_]*; diagnostic sink unavailable\.$') {
            $timestamp = [DateTimeOffset]::MinValue
            if ($null -ne $totals -or ![DateTimeOffset]::TryParseExact($Matches[1], 'O', [Globalization.CultureInfo]::InvariantCulture,
                [Globalization.DateTimeStyles]::None, [ref]$timestamp)) { throw 'Executed-test output is inconsistent or unsuccessful: invalid diagnostic.' }
            $diagnostics++; continue
        }
        $status = ''; $name = ''; $reason = ''
        if ($line -cmatch '^PASS (\S(?:.*\S)?)$') { $status = 'PASS'; $name = $Matches[1] }
        elseif ($line -cmatch '^(SKIP|FAIL) (\S(?:.*?\S)?): (\S(?:.*\S)?)$') {
            $status = $Matches[1]; $name = $Matches[2]; $reason = $Matches[3]
        }
        else { throw "Executed-test output is inconsistent or unsuccessful: unrecognized line: $line" }
        if ($null -ne $totals -or !$names.Add($name)) {
            throw "Executed-test output is inconsistent or unsuccessful: late or duplicate result: $name"
        }
        $records.Add([pscustomobject]@{Status=$status;Name=$name;Reason=$reason;Raw=$line})
    }
    if ($null -eq $totals) { throw 'Executed-test output is inconsistent or unsuccessful: missing totals.' }
    $total = $totals[0]; $passed = $totals[1]; $failed = $totals[2]; $skipped = $totals[3]
    $passes = @($records | Where-Object Status -CEQ 'PASS')
    $failures = @($records | Where-Object Status -CEQ 'FAIL')
    $skips = @($records | Where-Object Status -CEQ 'SKIP')
    if ($total -eq 0 -or $total -ne ([long]$passed + $failed + $skipped) -or $records.Count -ne $total -or
        $passes.Count -ne $passed -or $failures.Count -ne $failed -or $skips.Count -ne $skipped -or
        $failed -ne 0 -or $notTested -eq 0) {
        throw 'Executed-test output is inconsistent or unsuccessful: counts, missing results or failures.'
    }
    $unixNames = @(Get-UnixPermissionTestNames)
    if ($Platform -ceq 'Linux') {
        if ($skipped -ne 0) { throw 'Executed-test output is inconsistent or unsuccessful: Linux SKIP is forbidden.' }
        foreach ($unixName in $unixNames) {
            if (@($passes | Where-Object Name -CEQ $unixName).Count -ne 1) {
                throw "Executed-test output is inconsistent or unsuccessful: missing Linux permission PASS: $unixName"
            }
        }
    } else {
        if ($skips.Count -ne $unixNames.Count) { throw 'Executed-test output is inconsistent or unsuccessful: Windows requires exactly two permission SKIPs.' }
        foreach ($unixName in $unixNames) {
            $allowed = @($skips | Where-Object Name -CEQ $unixName)
            if ($allowed.Count -ne 1 -or $allowed[0].Reason -cne 'Unix permissions test; Windows ACL validation is separate') {
                throw "Executed-test output is inconsistent or unsuccessful: unauthorized Windows SKIP: $unixName"
            }
        }
    }
    [pscustomobject]@{Total=$total;Passed=$passed;Failed=$failed;Skipped=$skipped;Platform=$Platform;SkipRecords=$skips;DiagnosticLines=$diagnostics}
}

function Get-LogicTestResult([string]$Path) {
    Assert-NonemptyFile $Path
    $platform = if ([Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([Runtime.InteropServices.OSPlatform]::Windows)) {
        'Windows'
    } elseif ([Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([Runtime.InteropServices.OSPlatform]::Linux)) {
        'Linux'
    } else { throw 'Executed-test validation supports only Linux and Windows hosts.' }
    ConvertFrom-LogicTestOutput @(Get-Content -LiteralPath $Path) $platform
}

function Assert-PortableReadmeLinks([string]$Directory) {
    $readme = Join-Path $Directory 'README.md'
    Assert-NonemptyFile $readme
    $text = Get-Content -LiteralPath $readme -Raw
    $targets = [Collections.Generic.List[string]]::new()
    # Balanced parentheses, angle destinations and optional titles; unsupported link syntax fails closed.
    $inline = '!?(?:\[[^\]\r\n]*\])\(\s*(?:<(?<target>[^>\r\n]+)>|(?<target>(?:[^()\s\\]|\\.|(?<open>\()|(?<-open>\)))+)(?(open)(?!)))\s*(?:"[^"\r\n]*"|''[^''\r\n]*''|\([^\)\r\n]*\))?\s*\)'
    foreach ($match in [regex]::Matches($text, $inline)) { $targets.Add($match.Groups['target'].Value) }
    $remaining = [regex]::Replace($text, $inline, '')
    if ($remaining -match '\]\s*\(' -or $text -match '(?i)<(?:a|img)\b') {
        throw 'Unsupported Portable README link syntax.'
    }
    $definitions = @{}
    $definition = '(?m)^ {0,3}\[([^\]\r\n]+)\]:\s*(?:<([^>\r\n]+)>|(\S+))(?:\s+(?:"[^"\r\n]*"|''[^''\r\n]*''))?\s*$'
    foreach ($match in [regex]::Matches($remaining, $definition)) {
        $label = ($match.Groups[1].Value.Trim() -replace '\s+', ' ').ToLowerInvariant()
        if ($definitions.ContainsKey($label)) { throw "Duplicate Portable README link definition: $label" }
        $target = if ($match.Groups[2].Success) { $match.Groups[2].Value } else { $match.Groups[3].Value }
        $definitions[$label] = $target
        $targets.Add($target)
    }
    $remaining = [regex]::Replace($remaining, $definition, '')
    if ($remaining -match '(?m)^ {0,3}\[[^\]\r\n]+\]:') { throw 'Unsupported Portable README reference definition.' }
    foreach ($match in [regex]::Matches($remaining, '!?\[([^\]\r\n]+)\]\[([^\]\r\n]*)\]')) {
        $label = if ($match.Groups[2].Value) { $match.Groups[2].Value } else { $match.Groups[1].Value }
        $label = ($label.Trim() -replace '\s+', ' ').ToLowerInvariant()
        if (!$definitions.ContainsKey($label)) { throw "Missing Portable README link definition: $label" }
    }
    foreach ($match in [regex]::Matches($text, '(?i)<(https?://[^>\s]+)>')) { $targets.Add($match.Groups[1].Value) }
    $relativeCount = 0; $externalCount = 0
    foreach ($target in $targets) {
        $target = [Net.WebUtility]::HtmlDecode(($target -replace '\\([!"#$%&''()*+,\-./:;<=>?@\[\]\\^_`{|}~])', '$1'))
        if ($target -match '^(?i)https?://') {
            $uri = $null
            if (![Uri]::TryCreate($target, [UriKind]::Absolute, [ref]$uri) -or !$uri.Host) { throw "Invalid external README link: $target" }
            # External HTTP(S) links are classified, never mistaken for package files or fetched during release.
            $externalCount++; continue
        }
        $local = [Uri]::UnescapeDataString(($target -split '#', 2)[0])
        if (!$local) { $local = 'README.md' }
        if ($local.Contains('\') -or $local.Contains(':') -or $local.Contains('?') -or $local.StartsWith('/')) {
            throw "Unsafe Portable README link: $target"
        }
        $full = [IO.Path]::GetFullPath((Join-Path $Directory $local))
        $relative = [IO.Path]::GetRelativePath($Directory, $full).Replace('\','/')
        Assert-PortableFileName $relative
        Assert-NonemptyFile $full
        if ((Get-Item -LiteralPath $full).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "README link targets a reparse point: $target" }
        # Require exact package casing even on the Windows Runner.
        $packageNames = @(Get-PortableFiles $Directory | ForEach-Object { [IO.Path]::GetRelativePath($Directory, $_.FullName).Replace('\','/') })
        if ($relative -cnotin $packageNames) { throw "README link is not a packaged file: $target" }
        $relativeCount++
    }
    [pscustomobject]@{RelativeLinks=$relativeCount;ExternalHttpLinks=$externalCount}
}

function Copy-PortableUserGuide([string]$Directory) {
    $source = Join-Path $PSScriptRoot '../docs/PORTABLE-USER-GUIDE.md'
    Assert-NonemptyFile $source
    Copy-Item -LiteralPath $source -Destination (Join-Path $Directory 'README.md') -Force
    Assert-PortableReadmeLinks $Directory
}

function Write-PublishManifest([string]$Directory, [string]$ManifestPath) {
    # Documentation is final and links are checked before this first manifest/hash generation.
    $null = Assert-PortableReadmeLinks $Directory
    Get-PortableFiles $Directory | ForEach-Object {
        [pscustomobject]@{Path=[IO.Path]::GetRelativePath($Directory,$_.FullName).Replace('\','/');Bytes=$_.Length;
            SHA256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}
    } | Export-Csv -LiteralPath $ManifestPath -NoTypeInformation -Encoding utf8
    Assert-PublishManifest $Directory $ManifestPath
}

function Get-PortableTestSummary([string]$Directory) {
    $logic = Get-LogicTestResult (Join-Path $Directory 'LOGIC-TESTS.txt')
    Format-PortableTestSummary $Directory $logic
}

function Format-PortableTestSummary([string]$Directory, [object]$Logic) {
    # Renderer for already-validated results; fixtures can test Windows rendering on Linux.
    # The release entry point above always validates using the actual host OS first.
    $result = [Collections.Generic.List[string]]::new()
    $result.Add('Portable validation evidence; captured process output, not Windows 11 desktop acceptance.')
    $result.Add("Executed logic tests: TOTAL $($Logic.Total); PASSED $($Logic.Passed); FAILED $($Logic.Failed); SKIPPED $($Logic.Skipped)")
    $result.Add('NOT TESTED: Windows ACL validation is separate; Unix permission results do not validate Windows ACLs.')
    foreach ($name in @('LOGIC-TESTS.txt','PUBLISH-TESTS.txt','INITIALIZATION-TEST.txt')) {
        $path = Join-Path $Directory $name
        Assert-NonemptyFile $path
        $lines = @(Get-Content -LiteralPath $path)
        if ($name -ne 'LOGIC-TESTS.txt') {
            $expected = if ($name -eq 'PUBLISH-TESTS.txt') {
                'PASS: .NET/WPF payload, Windows App SDK metadata/runtime, app-local CRT import presence.'
            } else { 'PASS: published WPF XAML initialization and native AppNotificationBuilder' }
            if (@($lines | Where-Object { $_ -ceq $expected }).Count -ne 1 -or
                @($lines | Where-Object { $_ -match '^PASS' }).Count -ne 1 -or
                @($lines | Where-Object { $_ -match '^(FAIL|SKIP)\b' }).Count -ne 0 -or
                @($lines | Where-Object { $_ -match '^NOT TESTED: .+' }).Count -eq 0) { throw "Missing or inconsistent execution evidence: $name" }
        }
        $result.Add("Report SHA256: $((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant())  $name")
        $result.Add("--- $name ---")
        foreach ($line in $lines) { $result.Add($line) }
    }
    $result.Add('NOT TESTED: Windows 11 25H2 x64 26200.x real window/tray/notification display/click activation/sleep/Modern Standby.')
    $result.Add('NOT TESTED: real AppInstance exit ownership handoff/immediate restart, normal-user ACL, portable relocation, clean-machine loader, power loss/full disk/USB removal.')
    $result.Add('NOT TESTED: legacy normal-user install/uninstall; installer delivery has been cancelled.')
    $result.ToArray()
}

function Assert-TestSummary([string]$Directory) {
    $path = Join-Path $Directory 'TEST-SUMMARY.txt'
    Assert-NonemptyFile $path
    $actual = @(Get-Content -LiteralPath $path)
    $expected = @(Get-PortableTestSummary $Directory)
    if (($actual -join "`n") -cne ($expected -join "`n")) { throw 'TEST-SUMMARY does not match captured raw reports and their hashes.' }
}

function Assert-BuildInfo([string]$Path, [string]$ExpectedCommit) {
    Assert-NonemptyFile $Path
    if ($ExpectedCommit -notmatch '^[0-9a-fA-F]{40}$') { throw 'ExpectedCommit must be a full commit SHA.' }
    $values = @{}
    foreach ($line in Get-Content -LiteralPath $Path) {
        if ($line -notmatch '^([^:]+): (.+)$' -or $values.ContainsKey($Matches[1])) { throw 'Invalid or duplicate BUILD-INFO field.' }
        $values[$Matches[1]] = $Matches[2]
    }
    $expected = @{'Commit SHA'=$ExpectedCommit;'.NET SDK'='10.0.403';'.NET runtime'='10.0.14';
        'Windows App SDK'='Microsoft.WindowsAppSDK 2.6.5 Stable';'Windows SDK servicing baseline'='10.0.26100.9457';
        'Windows managed reference package'='10.0.26100.57';'Windows Target Platform Version'='10.0.26100.0';
        'Entry point'='PomodoroTimer.exe';'AssemblyName'='PomodoroTimerSpike';'TFM'='net10.0-windows10.0.26100.0';
        'RID'='win-x64';'Configuration'='Release';'.NET SelfContained'='true';'WindowsAppSDKSelfContained'='true';
        'WindowsPackageType'='None';'Runner'='windows-2025-vs2026 (Windows Server 2025; not Windows 11 desktop)';
        'Target desktop'='Windows 11 25H2 x64 26200.x, NOT TESTED'}
    foreach ($key in $expected.Keys) {
        if (!$values.ContainsKey($key) -or $values[$key] -cne $expected[$key]) { throw "BUILD-INFO mismatch: $key" }
    }
    foreach ($key in @('Runner image','Actual build OS','Built UTC','Windows SDK report SHA256','Deployment properties SHA256')) {
        if (!$values.ContainsKey($key) -or !$values[$key]) { throw "BUILD-INFO missing $key" }
    }
    $built = [DateTimeOffset]::MinValue
    if (![DateTimeOffset]::TryParse($values['Built UTC'], [ref]$built) -or $built.Offset -ne [TimeSpan]::Zero) { throw 'BUILD-INFO UTC timestamp invalid.' }
    foreach ($key in @('Windows SDK report SHA256','Deployment properties SHA256')) {
        if ($values[$key] -notmatch '^[0-9a-f]{64}$') { throw "Invalid BUILD-INFO hash: $key" }
    }
    $values
}

function Write-FileChecksums([string]$Directory, [string[]]$Names, [string]$Output) {
    $lines = foreach ($name in $Names | Sort-Object) {
        Assert-SafeRelativePath $name
        $path = Join-Path $Directory $name
        Assert-NonemptyFile $path
        "$((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant())  $name"
    }
    $lines | Set-Content -LiteralPath $Output -Encoding utf8
}

function Assert-FileChecksums([string]$Directory, [string[]]$ExpectedNames) {
    $path = Join-Path $Directory 'SHA256SUMS.txt'
    Assert-NonemptyFile $path
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($line in Get-Content -LiteralPath $path) {
        if ($line -notmatch '^([0-9a-f]{64})  (.+)$') { throw "Invalid checksum line: $line" }
        $hash = $Matches[1]; $name = $Matches[2]
        Assert-SafeRelativePath $name
        if (!$seen.Add($name) -or $name -cnotin $ExpectedNames) { throw "Unexpected or duplicate checksum target: $name" }
        $file = Join-Path $Directory $name
        Assert-NonemptyFile $file
        if ((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant() -cne $hash) { throw "SHA256 mismatch: $name" }
    }
    if ($seen.Count -ne $ExpectedNames.Count) { throw 'Checksum coverage incomplete.' }
}

function Assert-PublishManifest([string]$Directory, [string]$ManifestPath) {
    Assert-NonemptyFile $ManifestPath
    $rows = @(Import-Csv -LiteralPath $ManifestPath)
    $files = @(Get-PortableFiles $Directory | Where-Object {
        [IO.Path]::GetRelativePath($Directory, $_.FullName).Replace('\','/') -notin ($PortableEvidenceFiles + 'SHA256SUMS.txt')
    })
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($row in $rows) {
        Assert-PortableFileName $row.Path
        if ($row.Path -in ($PortableEvidenceFiles + 'SHA256SUMS.txt')) { throw 'Evidence cannot replace a runtime manifest entry.' }
        if (!$seen.Add($row.Path) -or $row.SHA256 -notmatch '^[0-9a-fA-F]{64}$' -or $row.Bytes -notmatch '^\d+$') { throw 'Invalid publish manifest row.' }
        $file = Join-Path $Directory $row.Path
        Assert-NonemptyFile $file
        if ((Get-Item -LiteralPath $file).Length -ne [long]$row.Bytes -or
            (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ine $row.SHA256) { throw "Publish manifest mismatch: $($row.Path)" }
    }
    if ($rows.Count -eq 0 -or $seen.Count -ne $files.Count) { throw 'Publish manifest coverage incomplete.' }
    foreach ($file in $files) {
        $name = [IO.Path]::GetRelativePath($Directory, $file.FullName).Replace('\','/')
        if (!$seen.Contains($name)) { throw "Unmanifested payload: $name" }
    }
}

function Assert-VcManifest([string]$Directory, [string]$ManifestPath) {
    Assert-NonemptyFile $ManifestPath
    $rows = @(Import-Csv -LiteralPath $ManifestPath)
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($row in $rows) {
        Assert-SafeRelativePath $row.File
        if ($row.File.Contains('/') -or !$seen.Add($row.File) -or !$row.Source -or !$row.Version -or
            $row.SHA256 -notmatch '^[0-9a-fA-F]{64}$') { throw 'Invalid VC runtime manifest.' }
        $file = Join-Path $Directory $row.File
        Assert-NonemptyFile $file
        if ((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ine $row.SHA256) { throw "VC runtime hash mismatch: $($row.File)" }
    }
    foreach ($name in @('msvcp140.dll','vcruntime140.dll','vcruntime140_1.dll')) {
        if (!$seen.Contains($name)) { throw "VC runtime manifest missing $name" }
    }
}

function Assert-PortableEvidence([string]$Directory, [string]$ExpectedCommit) {
    foreach ($name in $PortableEvidenceFiles) { Assert-NonemptyFile (Join-Path $Directory $name) }
    $info = Assert-BuildInfo (Join-Path $Directory 'BUILD-INFO.txt') $ExpectedCommit
    Assert-DeploymentProperties (Join-Path $Directory 'DEPLOYMENT-PROPERTIES.json')
    foreach ($pair in @(@('WINDOWS-SDK-INFO.txt','Windows SDK report SHA256'),
        @('DEPLOYMENT-PROPERTIES.json','Deployment properties SHA256'))) {
        if ((Get-FileHash -LiteralPath (Join-Path $Directory $pair[0]) -Algorithm SHA256).Hash.ToLowerInvariant() -cne $info[$pair[1]]) {
            throw "BUILD-INFO evidence hash mismatch: $($pair[0])"
        }
    }
    if ((Get-Content -LiteralPath (Join-Path $Directory 'WINDOWS-SDK-INFO.txt') -Raw) -notmatch '26100\.9457') {
        throw 'Fixed Windows SDK report does not identify the required baseline.'
    }
    Assert-TestSummary $Directory
}

function Get-ArtifactVerificationResult([string]$ZipHash) {
    "PASS: Portable ZIP paths, nonempty payload, AMD64 apphost/runtime, deployment identity, manifests, raw test reports and SHA256. ZIP SHA256: $ZipHash"
    'NOT TESTED: actual Windows loader, Windows 11 25H2 desktop/tray/notification display/activation, clean-machine operation and portable relocation.'
}

function Assert-PipelineScriptReport([string]$Directory) {
    $path = Join-Path $Directory 'PIPELINE-SCRIPT-TESTS.txt'
    Assert-NonemptyFile $path
    $lines = @(Get-Content -LiteralPath $path)
    $totals = @($lines | Where-Object { $_ -match '^VALIDATOR TOTAL \d+; PASSED \d+; FAILED \d+; SKIPPED \d+$' })
    if ($totals.Count -ne 1 -or $totals[0] -notmatch '^VALIDATOR TOTAL (\d+); PASSED (\d+); FAILED (\d+); SKIPPED (\d+)$') {
        throw 'Invalid validator test report totals.'
    }
    $total = [int]$Matches[1]; $passed = [int]$Matches[2]; $failed = [int]$Matches[3]; $skipped = [int]$Matches[4]
    $passes = @($lines | Where-Object { $_ -match '^PASS validator(?: rejection)?: .+' })
    if ($total -eq 0 -or $total -ne $passed -or $failed -ne 0 -or $skipped -ne 0 -or $passes.Count -ne $passed -or
        @($passes | Sort-Object -Unique).Count -ne $passed -or
        @($lines | Where-Object { $_ -match '^SYNTHETIC VALIDATOR TESTS ONLY;' }).Count -ne 1 -or
        @($lines | Where-Object { $_ -match '^NOT TESTED: .+' }).Count -eq 0) { throw 'Inconsistent validator test report.' }
    $logicPath = Join-Path $Directory 'LOGIC-TESTS.txt'
    $logic = Get-LogicTestResult $logicPath
    $expected = "Real input logic report: TOTAL $($logic.Total); PASSED $($logic.Passed); FAILED $($logic.Failed); SKIPPED $($logic.Skipped); SHA256 $((Get-FileHash -LiteralPath $logicPath -Algorithm SHA256).Hash.ToLowerInvariant())"
    if (@($lines | Where-Object { $_ -ceq $expected }).Count -ne 1) { throw 'Validator report is not bound to the captured logic output.' }
}
