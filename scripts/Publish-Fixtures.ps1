#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Publishes fixture files to an orphan 'fixtures' branch.

.DESCRIPTION
    Creates or force-updates the 'fixtures' orphan branch with the contents of
    EggIncognito/Fixtures/. The branch contains only fixture data - no source code.

    At runtime (e.g., in Docker), clone this branch to get fixture data:
      git clone --branch fixtures --depth 1 <repo-url> /app/Fixtures

.PARAMETER Remote
    Git remote to push to. Defaults to 'origin'.

.PARAMETER NoPush
    Stage and commit locally without pushing to the remote.
#>
[CmdletBinding()]
param(
    [string] $Remote = 'origin',
    [switch] $NoPush
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$fixturesDir = Join-Path $repoRoot 'EggIncognito' 'Fixtures'

if (-not (Test-Path $fixturesDir)) {
    Write-Error "Fixtures directory not found: $fixturesDir"
    exit 1
}

Push-Location $repoRoot
try {
    $tmpBranch = "publish-fixtures-tmp-$(Get-Random)"

    git checkout --orphan $tmpBranch
    git rm -rf . --quiet

    $destDir = Join-Path $repoRoot 'Fixtures'
    Copy-Item -Path $fixturesDir -Destination $destDir -Recurse -Force

    git add Fixtures/
    git commit -m "fixtures: publish fixture data"

    git branch -D fixtures 2>$null
    git branch -m fixtures

    if (-not $NoPush) {
        git push $Remote fixtures --force
        Write-Host "Pushed fixtures branch to $Remote." -ForegroundColor Green
    } else {
        Write-Host "fixtures branch created locally (skipped push)." -ForegroundColor Green
    }
} finally {
    git checkout main --quiet 2>$null
    Pop-Location
}
