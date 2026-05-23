#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Generates and publishes mock servers to per-language branches.

.DESCRIPTION
    For each supported language, runs CodeGen then pushes the output to a
    'generated/<lang>' branch. Users who want a specific language server can
    clone just that branch:

      git clone --branch generated/go --depth 1 <repo-url> egg-inc-mock-go

.PARAMETER Languages
    Languages to publish. Defaults to all supported languages.

.PARAMETER Remote
    Git remote to push to. Defaults to 'origin'.

.PARAMETER Bake
    Bake fixtures into binary .binpb files before generating.

.PARAMETER NoPush
    Commit locally without pushing to the remote.
#>
[CmdletBinding()]
param(
    [string[]] $Languages = @('go', 'python', 'javascript', 'java', 'kotlin', 'ruby', 'csharp'),
    [string] $Remote = 'origin',
    [switch] $Bake,
    [switch] $NoPush
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$sourceRef = git -C $repoRoot rev-parse --short HEAD

Write-Host "Building..."
dotnet build "$repoRoot\EggIncognito.slnx" --verbosity minimal
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

foreach ($lang in $Languages) {
    $branch = "generated/$lang"
    $outDir = Join-Path $repoRoot "generated\$lang"

    Write-Host ""
    Write-Host "=== $lang ===" -ForegroundColor Cyan

    $genArgs = @('run', '--project', "$repoRoot\EggIncognito.CodeGen", '--no-build', '--', 'generate', $lang)
    if ($Bake) { $genArgs += '--bake' }
    dotnet @genArgs
    if ($LASTEXITCODE -ne 0) { Write-Warning "Generation failed for $lang - skipping."; continue }

    $worktree = Join-Path ([System.IO.Path]::GetTempPath()) "ei-pub-$lang"
    if (Test-Path $worktree) { Remove-Item $worktree -Recurse -Force }

    git -C $repoRoot ls-remote --exit-code --heads $Remote $branch 2>$null
    if ($LASTEXITCODE -eq 0) {
        git -C $repoRoot worktree add $worktree $branch
    } else {
        git -C $repoRoot worktree add --orphan -b $branch $worktree
    }

    Get-ChildItem $worktree -Exclude '.git' | Remove-Item -Recurse -Force
    Copy-Item "$outDir\*" $worktree -Recurse -Force

    git -C $worktree add -A
    $null = git -C $worktree diff --cached --quiet 2>&1; $changed = $LASTEXITCODE -ne 0
    if ($changed) {
        git -C $worktree -c user.name='EggIncognito' -c user.email='noreply@localhost' `
            commit -m "generated: publish $lang stubs (main@$sourceRef)"
        if (-not $NoPush) {
            git -C $worktree push $Remote $branch
            Write-Host "Pushed $branch." -ForegroundColor Green
        } else {
            Write-Host "$branch committed locally (skipped push)." -ForegroundColor Green
        }
    } else {
        Write-Host "No changes for $lang." -ForegroundColor Yellow
    }

    git -C $repoRoot worktree remove $worktree --force
}

Write-Host ""
Write-Host "Done." -ForegroundColor Green
