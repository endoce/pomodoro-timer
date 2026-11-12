param([Parameter(Mandatory)][string]$ArtifactDirectory,
      [Parameter(Mandatory)][string]$ExpectedCommit,
      [switch]$RequireVerificationReport)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'portable-validation.ps1')
$ArtifactDirectory = (Resolve-Path -LiteralPath $ArtifactDirectory).Path
$zipPath = Join-Path $ArtifactDirectory $PortableZipName
Assert-NonemptyFile $zipPath
if (@(Get-ChildItem -LiteralPath $ArtifactDirectory -File -Filter '*.zip').Count -ne 1 -or
    @(Get-ChildItem -LiteralPath $ArtifactDirectory -File -Filter '*.exe').Count -ne 0) {
    throw 'Only the single named Portable ZIP may be a release payload; no installer/other ZIP allowed.'
}
$externalNames = @($PortableEvidenceFiles + $PortableZipName)
if ($RequireVerificationReport) {
    Assert-NonemptyFile (Join-Path $ArtifactDirectory 'ARTIFACT-TESTS.txt')
    $externalNames += 'ARTIFACT-TESTS.txt'
    $externalNames += 'PIPELINE-SCRIPT-TESTS.txt'
}
Assert-FileChecksums $ArtifactDirectory $externalNames
Assert-PortableEvidence $ArtifactDirectory $ExpectedCommit
$zipHash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
if ($RequireVerificationReport) {
    Assert-PipelineScriptReport $ArtifactDirectory
    $actual = @(Get-Content -LiteralPath (Join-Path $ArtifactDirectory 'ARTIFACT-TESTS.txt'))
    if (($actual -join "`n") -cne ((Get-ArtifactVerificationResult $zipHash) -join "`n")) {
        throw 'Artifact verification report does not identify this ZIP or preserve NOT TESTED.'
    }
}

$temporary = Join-Path ([IO.Path]::GetTempPath()) ('pomodoro-verify-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporary | Out-Null
try {
    $archive = [IO.Compression.ZipFile]::OpenRead($zipPath)
    try {
        if ($archive.Entries.Count -eq 0 -or $archive.Entries.Count -gt 20000) { throw 'Empty or excessive ZIP entry count.' }
        $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        $files = [Collections.Generic.List[string]]::new()
        [long]$expandedBytes = 0
        foreach ($entry in $archive.Entries) {
            $path = $entry.FullName
            Assert-SafeRelativePath $path
            if (!$seen.Add($path.TrimEnd('/'))) { throw "Duplicate ZIP entry: $path" }
            # Reject Unix symlinks, Windows reparse points and cross-platform path ambiguities before extraction.
            if ((($entry.ExternalAttributes -shr 16) -band 0xf000) -eq 0xa000 -or
                ($entry.ExternalAttributes -band 0x400) -ne 0) { throw "ZIP link/reparse entry rejected: $path" }
            if (!$path.StartsWith("$PortableFolderName/", [StringComparison]::Ordinal)) { throw "Unexpected ZIP root: $path" }
            $relative = $path.Substring($PortableFolderName.Length + 1)
            if ($entry.Name -eq '') {
                if ($entry.Length -ne 0) { throw "Directory ZIP entry has content: $path" }
                if ($relative) { Assert-PortableFileName $relative.TrimEnd('/') }
                continue
            }
            Assert-PortableFileName $relative
            if ($entry.Length -le 0 -or $entry.Length -gt 1GB) { throw "Empty or excessive ZIP file: $path" }
            $expandedBytes += $entry.Length
            if ($expandedBytes -gt 4GB) { throw 'ZIP expanded size exceeds safety limit.' }
            $files.Add($relative)
        }
        # Detect a file used as an ancestor directory (including case-only conflicts).
        foreach ($file in $files) {
            $parts = $file.Split('/')
            for ($i = 1; $i -lt $parts.Length; $i++) {
                $ancestor = $parts[0..($i - 1)] -join '/'
                if ($files.Contains($ancestor) -or @($files | Where-Object { $_ -ieq $ancestor }).Count -ne 0) {
                    throw "ZIP file/directory conflict: $file"
                }
            }
        }
        if ($files.Count -eq 0) { throw 'ZIP contains no files.' }
        [IO.Compression.ZipFileExtensions]::ExtractToDirectory($archive, $temporary)
    } finally { $archive.Dispose() }
    $payload = Join-Path $temporary $PortableFolderName
    foreach ($file in Get-ChildItem -LiteralPath $payload -Force -Recurse -File) {
        Assert-PortableFileName ([IO.Path]::GetRelativePath($payload, $file.FullName).Replace('\','/'))
        Assert-NonemptyFile $file.FullName
    }
    Assert-PortablePayload $payload
    $null = Assert-PortableReadmeLinks $payload
    Assert-PortableEvidence $payload $ExpectedCommit
    Assert-PublishManifest $payload (Join-Path $payload 'PUBLISH-MANIFEST.csv')
    Assert-VcManifest $payload (Join-Path $payload 'VC-RUNTIME.csv')
    Assert-FileChecksums $payload @($files | Where-Object { $_ -cne 'SHA256SUMS.txt' })
    foreach ($name in $PortableEvidenceFiles) {
        if ((Get-FileHash -LiteralPath (Join-Path $payload $name) -Algorithm SHA256).Hash -cne
            (Get-FileHash -LiteralPath (Join-Path $ArtifactDirectory $name) -Algorithm SHA256).Hash) {
            throw "Inside/outside evidence mismatch: $name"
        }
    }
    Get-ArtifactVerificationResult $zipHash
} finally {
    Remove-Item -LiteralPath $temporary -Recurse -Force
}
