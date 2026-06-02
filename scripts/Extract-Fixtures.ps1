#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Extract Egg Inc API fixture responses from mitmproxy capture files.

.DESCRIPTION
    Exports captured flows to HAR via mitmdump, then decodes each protobuf
    response and writes fixture JSON to EggIncognito/Fixtures/default/.

    If -FlowsFile is omitted, scans captures/ for all unprocessed .mitm files
    and processes each one in turn. After processing, each .mitm is filtered to
    auxbrain.com-only traffic and moved to captures/_processed/.

    Set EGG_INC_EID to scrub the real EID from all fixture output.

.PARAMETER FlowsFile
    Path to a specific mitmproxy capture file. If omitted, auto-scans captures/.

.PARAMETER Overwrite
    Overwrite existing fixture files when content differs.

.PARAMETER Eid
    EID to scrub from output. If omitted, the script looks for an EID in the
    filename (e.g. session_EI4765194876354560.mitm) then falls back to
    the EGG_INC_EID environment variable. Required when processing multiple
    captures with different EIDs - embed the EID in each filename instead.
#>
[CmdletBinding()]
param(
    [string] $FlowsFile,
    [switch] $Overwrite,
    [string] $Eid
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not (Get-Command mitmdump -ErrorAction SilentlyContinue)) {
    Write-Error "mitmdump not found in PATH. Install mitmproxy: https://mitmproxy.org/"
    exit 1
}

function Resolve-Eid([string] $filePath) {
    if ($Eid) { return $Eid }
    $m = [System.Text.RegularExpressions.Regex]::Match($filePath, 'EI\d{16,}')
    if ($m.Success) { return $m.Value }
    return $env:EGG_INC_EID
}

$repoRoot    = Split-Path -Parent $PSScriptRoot
$capturesDir = Join-Path $repoRoot 'captures'
$processedDir = Join-Path $capturesDir '_processed'

function Invoke-ExtractSingle([string] $mitm) {
    Write-Host ""
    Write-Host "=== $(Split-Path -Leaf $mitm) ===" -ForegroundColor Cyan

    $resolvedEid = Resolve-Eid $mitm
    if ($resolvedEid) {
        Write-Host "EID: $resolvedEid"
        $env:EGG_INC_EID = $resolvedEid
    } else {
        $env:EGG_INC_EID = $null
        Write-Host "EID: (not set - no scrubbing)" -ForegroundColor Yellow
    }

    $harFile = [System.IO.Path]::ChangeExtension([System.IO.Path]::GetTempFileName(), '.har')
    try {
        Write-Host "Exporting HAR from: $mitm"
        mitmdump -q -r $mitm --set hardump=$harFile 2>&1 | Out-Null
        if ($LASTEXITCODE -ne 0) { Write-Warning "mitmdump HAR export failed - skipping."; return }

        $seederArgs = @(
            'run', '--project', (Join-Path $repoRoot 'EggIncognito.Seeder'),
            '--', '--from-har', $harFile
        )
        if ($Overwrite) { $seederArgs += '--overwrite' }

        dotnet @seederArgs
        if ($LASTEXITCODE -ne 0) { Write-Warning "Seeder failed for $mitm." }
    } finally {
        Remove-Item -Path $harFile -Force -ErrorAction SilentlyContinue
    }

    # Filter to auxbrain.com and archive
    New-Item -ItemType Directory -Force -Path $processedDir | Out-Null
    $archiveName = [System.IO.Path]::GetFileName($mitm)
    $archivePath = Join-Path $processedDir $archiveName

    Write-Host "Filtering and archiving to _processed/$archiveName..."
    mitmdump -q -r $mitm -w $archivePath '~d auxbrain.com' 2>&1 | Out-Null
    if ($LASTEXITCODE -eq 0) {
        Remove-Item $mitm -Force
        Write-Host "Archived." -ForegroundColor Green
    } else {
        Write-Warning "Filter failed - leaving original in place."
    }
}

if ($FlowsFile) {
    if (-not (Test-Path $FlowsFile)) {
        Write-Error "File not found: $FlowsFile"
        exit 1
    }
    Invoke-ExtractSingle (Resolve-Path $FlowsFile).Path
} else {
    $unprocessed = Get-ChildItem $capturesDir -Filter '*.mitm' -ErrorAction SilentlyContinue |
                   Where-Object { $_.DirectoryName -notlike "*_processed*" }

    if ($unprocessed.Count -eq 0) {
        Write-Host "No unprocessed .mitm files found in $capturesDir." -ForegroundColor Yellow
        exit 0
    }

    Write-Host "Found $($unprocessed.Count) unprocessed capture(s)."
    foreach ($f in $unprocessed) {
        Invoke-ExtractSingle $f.FullName
    }
}

Write-Host ""
Write-Host "Done." -ForegroundColor Green
