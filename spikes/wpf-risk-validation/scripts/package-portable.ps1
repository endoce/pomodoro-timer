param([Parameter(Mandatory)][string]$PublishDirectory,
      [Parameter(Mandatory)][string]$ArtifactDirectory,
      [Parameter(Mandatory)][string]$ExpectedCommit,
      [Parameter(Mandatory)][string]$WindowsSdkReport)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'portable-validation.ps1')
if (!$IsWindows) { throw 'Product packaging requires the Windows Runner; Linux fixtures are not product releases.' }
if ((dotnet --version) -cne '10.0.403' -or $LASTEXITCODE -ne 0) { throw 'SDK mismatch during packaging.' }
if (!$env:ImageVersion) { throw 'Runner image version missing.' }
$PublishDirectory = (Resolve-Path -LiteralPath $PublishDirectory).Path
$ArtifactDirectory = (Resolve-Path -LiteralPath $ArtifactDirectory).Path
$head = & git rev-parse HEAD
if ($LASTEXITCODE -ne 0 -or $head -cne $ExpectedCommit) { throw 'Checkout identity does not match ExpectedCommit.' }
$dirty = & git status --porcelain --untracked-files=normal
if ($LASTEXITCODE -ne 0 -or $dirty) { throw 'Cannot label a modified/untracked checkout as the committed release source.' }
if (Test-Path -LiteralPath (Join-Path $ArtifactDirectory $PortableZipName)) { throw 'Portable ZIP already exists; use a fresh build directory.' }
Assert-NonemptyFile $WindowsSdkReport
Copy-Item -LiteralPath $WindowsSdkReport -Destination (Join-Path $ArtifactDirectory 'WINDOWS-SDK-INFO.txt')
Assert-PortablePayload $PublishDirectory
Assert-VcManifest $PublishDirectory (Join-Path $ArtifactDirectory 'VC-RUNTIME.csv')
Assert-DeploymentProperties (Join-Path $ArtifactDirectory 'DEPLOYMENT-PROPERTIES.json')
Assert-TestSummary $ArtifactDirectory
# Do not copy the repository's developer README or desktop acceptance documents into the product.
# Finalize the user README and check every local link before manifest, BUILD-INFO and inner hashes.
$null = Copy-PortableUserGuide $PublishDirectory
Write-PublishManifest $PublishDirectory (Join-Path $ArtifactDirectory 'PUBLISH-MANIFEST.csv')
@"
Commit SHA: $ExpectedCommit
.NET SDK: $(dotnet --version)
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
Runner image: $env:ImageVersion
Actual build OS: $([Environment]::OSVersion.Version)
Target desktop: Windows 11 25H2 x64 26200.x, NOT TESTED
Built UTC: $([DateTimeOffset]::UtcNow.ToString('O'))
Windows SDK report SHA256: $((Get-FileHash -LiteralPath $WindowsSdkReport -Algorithm SHA256).Hash.ToLowerInvariant())
Deployment properties SHA256: $((Get-FileHash -LiteralPath (Join-Path $ArtifactDirectory 'DEPLOYMENT-PROPERTIES.json') -Algorithm SHA256).Hash.ToLowerInvariant())
"@ | Set-Content -LiteralPath (Join-Path $ArtifactDirectory 'BUILD-INFO.txt') -Encoding utf8
Assert-PortableEvidence $ArtifactDirectory $ExpectedCommit

# Hash every staged file before creating the ZIP; metadata is never appended after packaging.
$temporary = Join-Path ([IO.Path]::GetTempPath()) ('pomodoro-pack-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporary | Out-Null
try {
    $stage = Join-Path $temporary $PortableFolderName
    New-Item -ItemType Directory -Path $stage | Out-Null
    foreach ($file in Get-PortableFiles $PublishDirectory) {
        $relative = [IO.Path]::GetRelativePath($PublishDirectory, $file.FullName).Replace('\','/')
        if ($relative -in ($PortableEvidenceFiles + 'SHA256SUMS.txt')) { throw "Unexpected pre-existing publish evidence: $relative" }
        $destination = Join-Path $stage $relative
        New-Item -ItemType Directory -Force -Path (Split-Path $destination -Parent) | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $destination
    }
    foreach ($name in $PortableEvidenceFiles) {
        Copy-Item -LiteralPath (Join-Path $ArtifactDirectory $name) -Destination (Join-Path $stage $name)
    }
    Assert-PortablePayload $stage
    $null = Assert-PortableReadmeLinks $stage
    Assert-PortableEvidence $stage $ExpectedCommit
    Assert-PublishManifest $stage (Join-Path $stage 'PUBLISH-MANIFEST.csv')
    Assert-VcManifest $stage (Join-Path $stage 'VC-RUNTIME.csv')
    $names = @(Get-PortableFiles $stage | ForEach-Object { [IO.Path]::GetRelativePath($stage, $_.FullName).Replace('\','/') })
    Write-FileChecksums $stage $names (Join-Path $stage 'SHA256SUMS.txt')
    Assert-FileChecksums $stage $names
    $zip = Join-Path $temporary $PortableZipName
    [IO.Compression.ZipFile]::CreateFromDirectory($stage, $zip, [IO.Compression.CompressionLevel]::Optimal, $true)
    Move-Item -LiteralPath $zip -Destination (Join-Path $ArtifactDirectory $PortableZipName)
    Write-FileChecksums $ArtifactDirectory ($PortableEvidenceFiles + $PortableZipName) (Join-Path $ArtifactDirectory 'SHA256SUMS.txt')
} finally {
    # Only this invocation's unique temporary directory is eligible for cleanup.
    Remove-Item -LiteralPath $temporary -Recurse -Force
}
