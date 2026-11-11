param([string]$ToolsDirectory = (Join-Path $PSScriptRoot '../tools'))
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
New-Item -ItemType Directory -Force $ToolsDirectory | Out-Null

function Get-SdkProduct {
    $roots = @('HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*',
        'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*')
    @(Get-ItemProperty $roots -ErrorAction SilentlyContinue | Where-Object {
        $_.PSObject.Properties.Name -contains 'DisplayName' -and
        $_.DisplayName -match 'Windows Software Development Kit' -and
        ($_.DisplayName -match '26100\.9457' -or
            ($_.PSObject.Properties.Name -contains 'DisplayVersion' -and $_.DisplayVersion -match '26100\.9457'))
    })
}

if (@(Get-SdkProduct).Count -eq 0) {
    $setup = Join-Path $ToolsDirectory 'winsdk-26100.9457.exe'
    Invoke-WebRequest 'https://go.microsoft.com/fwlink/?linkid=2382321' -OutFile $setup
    $signature = Get-AuthenticodeSignature $setup
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'Microsoft') {
        throw 'Windows SDK installer is not validly Microsoft-signed.'
    }
    $process = Start-Process $setup -ArgumentList '/quiet /norestart /features OptionId.WindowsDesktopSoftwareDevelopmentKit' -Wait -PassThru
    if ($process.ExitCode -notin @(0, 3010)) { throw "Windows SDK installation failed: $($process.ExitCode)" }
}
$products = @(Get-SdkProduct)
if ($products.Count -eq 0) { throw 'Exact Windows SDK 26100.9457 servicing product could not be confirmed.' }
$kits = ${env:ProgramFiles(x86)} + '\Windows Kits\10'
if (!(Test-Path "$kits\bin\10.0.26100.0\x64\rc.exe")) { throw '26100 platform build tools missing.' }
$products | Select-Object DisplayName, DisplayVersion | Format-List | Out-String |
    Set-Content (Join-Path $ToolsDirectory 'WINDOWS-SDK-INFO.txt')

# Pin the compiler download as well as its vendor's Authenticode identity.
$inno = Join-Path $ToolsDirectory 'innosetup-6.7.4.exe'
Invoke-WebRequest 'https://github.com/jrsoftware/issrc/releases/download/is-6_7_4/innosetup-6.7.4.exe' -OutFile $inno
$hash = (Get-FileHash $inno -Algorithm SHA256).Hash.ToLowerInvariant()
if ($hash -ne 'a549929319f9120716677c685535ca4671e4b937b5fdc87e2585f32d8eb30e8c') {
    throw 'Inno Setup installer SHA256 mismatch.'
}
$signature = Get-AuthenticodeSignature $inno
if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'Pyrsys') {
    throw 'Inno Setup publisher signature mismatch.'
}
$innoDirectory = Join-Path $ToolsDirectory 'inno'
$process = Start-Process $inno -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',"/DIR=`"$innoDirectory`"") -Wait -PassThru
if ($process.ExitCode -ne 0) { throw "Inno Setup installation failed: $($process.ExitCode)" }
if (!(Test-Path (Join-Path $innoDirectory 'ISCC.exe'))) { throw 'ISCC.exe missing.' }
Write-Output 'Fixed Windows SDK and Inno Setup compiler verified.'
