#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Extracts Egg Inc API endpoints from an APK and compares against endpoints.yaml.

.DESCRIPTION
    Obtains the arm64 split APK via one of three methods (in priority order):

      1. -ApkPath     : Use a local APK file directly.
      2. ADB          : Pull from a connected Android device.
      3. apkeep       : Download from Google Play (requires -GooglePlayEmail
                        and -GooglePlayToken, or a saved token file).

    Extracts endpoint paths from libegginc.so, then diffs against
    EndpointMap/endpoints.yaml.

    Endpoints in APK but not in yaml  -> shown as NEW  (green)
    Endpoints in yaml but not in APK  -> shown as REMOVED (yellow)

    With -Apply, new endpoints are appended to yaml with AuthenticatedMessage
    placeholder types. Fix types manually after.

.PARAMETER ApkPath
    Path to an existing arm64 APK (split_config.arm64_v8a.apk or any APK
    containing lib/arm64-v8a/libegginc.so).

.PARAMETER Apply
    Append new endpoints to endpoints.yaml with AuthenticatedMessage placeholders.

.PARAMETER OutputDir
    Directory for downloaded/pulled APKs. Defaults to ./apks relative to repo root.

.PARAMETER GooglePlayEmail
    Google account email for apkeep Google Play download.

.PARAMETER GooglePlayToken
    AAS token for apkeep. If omitted, the script checks the token file at
    ~/.config/EggIncognito/gp-token.txt. Obtain a token once with:

        apkeep -e your@gmail.com --oauth-token <oauth2_token>

    Then save the printed AAS token to ~/.config/EggIncognito/gp-token.txt.

.EXAMPLE
    # Use local APK
    .\Sync-Endpoints.ps1 -ApkPath .\apks\split_config.arm64_v8a.apk

.EXAMPLE
    # Pull from connected Android device via ADB
    .\Sync-Endpoints.ps1

.EXAMPLE
    # Download via apkeep (token saved in token file)
    .\Sync-Endpoints.ps1 -GooglePlayEmail you@gmail.com

.EXAMPLE
    # Download via apkeep and apply diff to yaml
    .\Sync-Endpoints.ps1 -GooglePlayEmail you@gmail.com -Apply
#>
[CmdletBinding()]
param(
    [string] $ApkPath,
    [switch] $Apply,
    [string] $OutputDir,
    [string] $GooglePlayEmail,
    [string] $GooglePlayToken
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$repoRoot = Split-Path -Parent $PSScriptRoot
$yamlPath = Join-Path $repoRoot 'EggIncognito' 'EndpointMap' 'endpoints.yaml'
$tokenFile = Join-Path $HOME '.config' 'EggIncognito' 'gp-token.txt'

if (-not (Test-Path $yamlPath)) {
    Write-Error "endpoints.yaml not found at: $yamlPath"
    exit 1
}

function Find-EggIncArm64Apk([string]$dir) {
    foreach ($f in Get-ChildItem $dir -Filter *.apk -Recurse -ErrorAction SilentlyContinue) {
        try {
            $z = [System.IO.Compression.ZipFile]::OpenRead($f.FullName)
            $has = $null -ne $z.GetEntry('lib/arm64-v8a/libegginc.so')
            $z.Dispose()
            if ($has) { return $f.FullName }
        } catch {}
    }
    return $null
}

$outDir = if ($OutputDir) { $OutputDir } else { Join-Path $repoRoot 'apks' }
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

if ($ApkPath) {
    if (-not (Test-Path $ApkPath)) {
        Write-Error "APK not found: $ApkPath"
        exit 1
    }
    Write-Host "Using local APK: $ApkPath"

} elseif (Get-Command adb -ErrorAction SilentlyContinue) {
    Write-Host "ADB found. Querying connected device for com.auxbrain.egginc..."
    $pmOutput = adb shell pm path com.auxbrain.egginc 2>&1

    if ($LASTEXITCODE -eq 0) {
        $splitLine = $pmOutput | Where-Object { $_ -match 'arm' } | Select-Object -First 1
        if ($splitLine) {
            $devicePath = ($splitLine -replace '^package:', '').Trim()
            Write-Host "Pulling: $devicePath"
            adb pull $devicePath $outDir
            if ($LASTEXITCODE -ne 0) { Write-Error "adb pull failed."; exit 1 }
            $ApkPath = Join-Path $outDir (Split-Path -Leaf $devicePath)
        } else {
            Write-Warning "ADB: no arm split APK in pm output. Falling through to next method."
        }
    } else {
        Write-Warning "ADB: no device/emulator found. Falling through to next method."
    }
}

if (-not $ApkPath -and $GooglePlayEmail) {
    if (-not (Get-Command apkeep -ErrorAction SilentlyContinue)) {
        Write-Error @"
apkeep not found in PATH. Install from: https://github.com/EFForg/apkeep/releases
Then set up a token once:
  apkeep -e $GooglePlayEmail --oauth-token <oauth2_token_from_google>
Save the printed AAS token to: $tokenFile
"@
        exit 1
    }

    if (-not $GooglePlayToken) {
        if (Test-Path $tokenFile) {
            $GooglePlayToken = (Get-Content $tokenFile -Raw).Trim()
            Write-Host "Loaded AAS token from: $tokenFile"
        } else {
            Write-Error @"
No Google Play AAS token provided and no token file found at:
  $tokenFile

Obtain a token once by running:
  apkeep -e $GooglePlayEmail --oauth-token <oauth2_token_from_google>

Then save the printed AAS token to: $tokenFile
"@
            exit 1
        }
    }

    Write-Host "Downloading com.auxbrain.egginc via apkeep (Google Play)..."
    apkeep -a com.auxbrain.egginc -d google-play -e $GooglePlayEmail -t $GooglePlayToken -o split_apk=true $outDir
    if ($LASTEXITCODE -ne 0) { Write-Error "apkeep download failed."; exit 1 }

    $ApkPath = Find-EggIncArm64Apk $outDir
    if (-not $ApkPath) {
        Write-Error "apkeep ran but no APK containing lib/arm64-v8a/libegginc.so found in: $outDir"
        exit 1
    }
    Write-Host "Found arm64 APK: $ApkPath"
}

if (-not $ApkPath) {
    Write-Host ""
    Write-Host "No APK source available. Options:" -ForegroundColor Yellow
    Write-Host "  1. Connect an Android device and ensure 'adb devices' shows it." -ForegroundColor Yellow
    Write-Host "  2. Download the arm64 split manually from APKMirror:" -ForegroundColor Yellow
    Write-Host "       https://www.apkmirror.com/apk/auxbrain-inc/egg-inc/" -ForegroundColor Cyan
    Write-Host "     Then run: .\Sync-Endpoints.ps1 -ApkPath <path-to-split_config.arm64_v8a.apk>" -ForegroundColor Cyan
    Write-Host "  3. Use apkeep (Google Play):" -ForegroundColor Yellow
    Write-Host "       apkeep -e you@gmail.com --oauth-token <token>   # get AAS token" -ForegroundColor Cyan
    Write-Host "       .\Sync-Endpoints.ps1 -GooglePlayEmail you@gmail.com" -ForegroundColor Cyan
    exit 1
}

Write-Host "Extracting endpoint paths from libegginc.so..."

$apkStream = [System.IO.File]::OpenRead($ApkPath)
$zip = [System.IO.Compression.ZipArchive]::new($apkStream, 'Read')
$soEntry = $zip.GetEntry('lib/arm64-v8a/libegginc.so')

if (-not $soEntry) {
    $zip.Dispose(); $apkStream.Dispose()
    Write-Error "lib/arm64-v8a/libegginc.so not found in: $ApkPath`nEnsure this is the arm64 split APK, not the base APK."
    exit 1
}

$soMem = [System.IO.MemoryStream]::new()
$soEntry.Open().CopyTo($soMem)
$zip.Dispose()
$apkStream.Dispose()

$pattern = [System.Text.RegularExpressions.Regex]::new(
    'ei(?:_afx|_ctx|_data|_srv)?/[a-z_0-9]+'
)
$text = [System.Text.Encoding]::Latin1.GetString($soMem.ToArray())
$soMem.Dispose()

$apkEndpoints = $pattern.Matches($text) | ForEach-Object { $_.Value } | Sort-Object -Unique
Write-Host "Found $($apkEndpoints.Count) endpoint(s) in APK."

$yamlContent = Get-Content $yamlPath -Raw
$yamlEndpoints = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
foreach ($m in [System.Text.RegularExpressions.Regex]::Matches($yamlContent, '(?m)^\s*- path:\s*(.+)$')) {
    $null = $yamlEndpoints.Add($m.Groups[1].Value.Trim())
}
Write-Host "Found $($yamlEndpoints.Count) endpoint(s) in endpoints.yaml."

$excludedEndpoints = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
$excludedSection = ($yamlContent -split '(?m)^excluded:')[1]
if ($excludedSection) {
    foreach ($m in [System.Text.RegularExpressions.Regex]::Matches($excludedSection, '(?m)^\s{2}-\s+(.+)$')) {
        $entry = $m.Groups[1].Value.Trim() -replace '\s*#.*$', ''
        $null = $excludedEndpoints.Add($entry)
    }
}
if ($excludedEndpoints.Count -gt 0) {
    Write-Host "Excluding $($excludedEndpoints.Count) suppressed endpoint(s)."
}

$newEndpoints = $apkEndpoints | Where-Object { -not $yamlEndpoints.Contains($_) -and -not $excludedEndpoints.Contains($_) }
$removedEndpoints = $yamlEndpoints | Where-Object { $p = $_; -not ($apkEndpoints -contains $p) } | Sort-Object

Write-Host ""
if ($newEndpoints) {
    Write-Host "NEW (in APK, not in yaml):" -ForegroundColor Green
    $newEndpoints | ForEach-Object { Write-Host "  + $_" -ForegroundColor Green }
} else {
    Write-Host "No new endpoints." -ForegroundColor Green
}

if ($removedEndpoints) {
    Write-Host ""
    Write-Host "REMOVED (in yaml, not in APK):" -ForegroundColor Yellow
    $removedEndpoints | ForEach-Object { Write-Host "  - $_" -ForegroundColor Yellow }
} else {
    Write-Host "No removed endpoints." -ForegroundColor Green
}

if (-not $newEndpoints -and -not $removedEndpoints) {
    Write-Host ""
    Write-Host "endpoints.yaml is in sync with the APK." -ForegroundColor Cyan
    exit 0
}

if (-not $Apply) {
    Write-Host ""
    Write-Host "Run with -Apply to append new endpoints to endpoints.yaml." -ForegroundColor Cyan
    Write-Host "Removed endpoints must be cleaned up manually." -ForegroundColor Cyan
    exit 0
}

if ($newEndpoints) {
    Write-Host ""
    Write-Host "Appending $($newEndpoints.Count) new endpoint(s) to endpoints.yaml..."
    $lines = @("", "  # Added by Sync-Endpoints.ps1 - review and set correct requestType/responseType")
    foreach ($ep in $newEndpoints) {
        $lines += "  - path: $ep"
        $lines += "    requestType: AuthenticatedMessage"
        $lines += "    responseType: AuthenticatedMessage"
    }
    Add-Content -Path $yamlPath -Value ($lines -join "`n")
    Write-Host "Done. Review types in endpoints.yaml before building." -ForegroundColor Green
}

if ($removedEndpoints) {
    Write-Host ""
    Write-Host "Skipped removal of $($removedEndpoints.Count) endpoint(s) - remove manually:" -ForegroundColor Yellow
    $removedEndpoints | ForEach-Object { Write-Host "  - $_" -ForegroundColor Yellow }
}
