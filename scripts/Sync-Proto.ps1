#!/usr/bin/env pwsh
<#
.SYNOPSIS
  Download the latest ei.proto from elgranjero/EggIncProtos.

.PARAMETER Force
  Re-download even if proto-version.txt matches the upstream commit SHA.
#>
param([switch]$Force)

$ErrorActionPreference = "Stop"

$ProtoPath = Join-Path $PSScriptRoot '..' 'EggIncognito' 'Proto' 'ei.proto'
$VersionPath = Join-Path $PSScriptRoot '..' 'EggIncognito' 'Proto' 'proto-version.txt'
$RepoApi = "https://api.github.com/repos/elgranjero/EggIncProtos/commits?path=ei.proto&per_page=1"
$RawProto = "https://raw.githubusercontent.com/elgranjero/EggIncProtos/main/ei.proto"

Write-Host "Checking upstream ei.proto..."

$currentSha = if (Test-Path $VersionPath) { (Get-Content $VersionPath -Raw).Trim() } else { "" }

$response = Invoke-RestMethod -Uri $RepoApi -Headers @{ "User-Agent" = "EggIncognito/sync" }
$latestSha = $response[0].sha

if (-not $Force -and $currentSha -eq $latestSha) {
    Write-Host "ei.proto is up to date (SHA: $latestSha). Use -Force to re-download."
    exit 0
}

Write-Host "Downloading ei.proto (SHA: $latestSha)..."
Invoke-WebRequest -Uri $RawProto -OutFile $ProtoPath
Set-Content -Path $VersionPath -Value $latestSha -NoNewline

Write-Host "Done. ei.proto updated to commit $latestSha"
Write-Host "IMPORTANT: Review changes before rebuilding - proto changes may break existing fixtures."
