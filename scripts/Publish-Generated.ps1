#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Generates and publishes mock servers to per-language orphan branches.

.DESCRIPTION
    For each supported language, runs 'dotnet run -- generate <lang> --bake' then
    force-pushes the output to a 'generated/<lang>' orphan branch. Users who want
    a specific language server can clone just that branch:

      git clone --branch generated/go --depth 1 <repo-url> egg-inc-mock-go

.PARAMETER Languages
    Languages to publish. Defaults to all supported languages.

.PARAMETER Remote
    Git remote to push to. Defaults to 'origin'.

.PARAMETER NoPush
    Stage and commit locally without pushing to the remote.
#>
[CmdletBinding()]
param(
    [string[]] $Languages = @('go', 'python', 'javascript', 'java', 'kotlin', 'ruby', 'csharp'),
    [string] $Remote = 'origin',
    [switch] $NoPush
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot

Push-Location $repoRoot
try {
    $originalBranch = git rev-parse --abbrev-ref HEAD

    foreach ($lang in $Languages) {
        Write-Host ""
        Write-Host "=== $lang ===" -ForegroundColor Cyan

        dotnet run --project EggIncognito.CodeGen -- generate $lang --bake
        if ($LASTEXITCODE -ne 0) {
            Write-Warning "Generation failed for $lang - skipping."
            continue
        }

        $generatedDir = Join-Path $repoRoot 'generated' $lang
        $branchName = "generated/$lang"
        $tmpBranch = "publish-$lang-tmp-$(Get-Random)"

        git checkout --orphan $tmpBranch
        git rm -rf . --quiet

        Copy-Item -Path (Join-Path $generatedDir '*') -Destination $repoRoot -Recurse -Force

        git add .
        git commit -m "generated: publish $lang mock server"

        git branch -D $branchName 2>$null
        git branch -m $branchName

        if (-not $NoPush) {
            git push $Remote $branchName --force
            Write-Host "Pushed $branchName to $Remote." -ForegroundColor Green
        } else {
            Write-Host "$branchName created locally (skipped push)." -ForegroundColor Green
        }

        git checkout $originalBranch --quiet
    }

    Write-Host ""
    Write-Host "Done." -ForegroundColor Green
} finally {
    git checkout $originalBranch --quiet 2>$null
    Pop-Location
}
