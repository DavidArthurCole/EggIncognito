#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Extract Egg Inc API fixture responses from a mitmproxy capture file.

.DESCRIPTION
    Exports the captured flows to HAR format using mitmdump, then invokes
    EggIncognito.Seeder in --from-har mode to decode each protobuf response
    and write fixture JSON files to EggIncognito/Fixtures/default/.

    Set EGG_INC_EID to have the real EID scrubbed from all fixture output.

.PARAMETER FlowsFile
    Path to the mitmproxy capture file (.mitm format).

.PARAMETER Overwrite
    Overwrite existing fixture files when content differs.

.EXAMPLE
    .\Extract-Fixtures.ps1 -FlowsFile .\flows.mitm

.EXAMPLE
    .\Extract-Fixtures.ps1 -FlowsFile .\flows.mitm -Overwrite
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $FlowsFile,
    [switch] $Overwrite
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not (Get-Command mitmdump -ErrorAction SilentlyContinue)) {
    Write-Error "mitmdump not found in PATH. Install mitmproxy: https://mitmproxy.org/"
    exit 1
}

if (-not (Test-Path $FlowsFile)) {
    Write-Error "Flows file not found: $FlowsFile"
    exit 1
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$harFile = [System.IO.Path]::ChangeExtension([System.IO.Path]::GetTempFileName(), '.har')

try {
    Write-Host "Exporting HAR from: $FlowsFile"
    mitmdump -q -r $FlowsFile -w $harFile "~d auxbrain.com"
    if ($LASTEXITCODE -ne 0) { Write-Error "mitmdump HAR export failed."; exit 1 }

    $seederArgs = @('run', '--project', (Join-Path $repoRoot 'EggIncognito.Seeder'), '--', '--from-har', $harFile)
    if ($Overwrite) { $seederArgs += '--overwrite' }

    dotnet @seederArgs
    exit $LASTEXITCODE
} finally {
    Remove-Item -Path $harFile -Force -ErrorAction SilentlyContinue
}
