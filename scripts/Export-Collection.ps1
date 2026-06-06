#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Generates a Postman v2.1 collection JSON from endpoints.yaml.

.DESCRIPTION
    Reads EggIncognito/EndpointMap/endpoints.yaml and produces a Postman
    collection with one request per endpoint, grouped by API namespace
    (ei, ei_afx, ei_ctx, ei_data, ei_srv).

    Each request is a POST with a 'data' form field. A pre-request script
    builds the AuthenticatedMessage proto bytes from the 'userId' collection
    variable and stores them in 'authData', which is used as the form value.

    Import the output file into Postman: File > Import > select the JSON.
    Set the 'baseUrl' and 'userId' collection variables before sending requests.

.PARAMETER OutputPath
    Path to write the collection JSON. Defaults to
    <repo-root>/EggIncognito-postman-collection.json.

.EXAMPLE
    ./scripts/Export-Collection.ps1

.EXAMPLE
    ./scripts/Export-Collection.ps1 -OutputPath ~/Desktop/egginc.json
#>
param([string] $OutputPath)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$yamlPath = Join-Path $repoRoot 'EggIncognito' 'EndpointMap' 'endpoints.yaml'
$protoPath = Join-Path $repoRoot 'EggIncognito' 'Proto' 'ei.proto'
$outPath = if ($OutputPath) { $OutputPath } else {
    Join-Path $repoRoot 'EggIncognito-postman-collection.json'
}

if (-not (Test-Path $yamlPath)) {
    Write-Error "endpoints.yaml not found at: $yamlPath"
    exit 1
}

$yaml = Get-Content $yamlPath -Raw

# Parse entries: each block "- path: X\n    requestType: Y\n    responseType: Z"
$pattern = [regex]::new('- path:\s+(\S+)\s+requestType:\s+(\S+)\s+responseType:\s+(\S+)')
$entries = $pattern.Matches($yaml) | ForEach-Object {
    @{
        Path = $_.Groups[1].Value
        RequestType = $_.Groups[2].Value
        ResponseType = $_.Groups[3].Value
    }
}

Write-Host "Loaded $($entries.Count) endpoint(s) from endpoints.yaml."

# Parse ei.proto to build a map of message name -> field lines
# Best-effort: captures "optional/repeated/required <type> <name> = <N>;"
$protoMessageFields = @{}

if (Test-Path $protoPath) {
    $protoText = Get-Content $protoPath -Raw

    # Match top-level message blocks (not nested ones - we only need the outer fields)
    # Strategy: find "message Name {" then scan until the matching closing brace
    $msgStartPattern = [regex]::new('message\s+(\w+)\s*\{')
    $fieldPattern = [regex]::new('(?m)^\s+(?:optional|repeated|required)\s+(\S+)\s+(\w+)\s*=\s*\d+')

    $matches = $msgStartPattern.Matches($protoText)
    foreach ($m in $matches) {
        $msgName = $m.Groups[1].Value
        $startPos = $m.Index + $m.Length

        # Find the matching closing brace by counting brace depth
        $depth = 1
        $pos = $startPos
        $chars = $protoText.ToCharArray()
        $len = $chars.Length
        while ($pos -lt $len -and $depth -gt 0) {
            if ($chars[$pos] -eq '{') { $depth++ }
            elseif ($chars[$pos] -eq '}') { $depth-- }
            $pos++
        }

        $blockContent = $protoText.Substring($startPos, $pos - $startPos - 1)

        $fields = $fieldPattern.Matches($blockContent) | ForEach-Object {
            "$($_.Groups[2].Value) $($_.Groups[1].Value)"
        }

        if ($fields.Count -gt 0 -and -not $protoMessageFields.ContainsKey($msgName)) {
            $protoMessageFields[$msgName] = @($fields)
        }
    }

    Write-Host "Parsed $($protoMessageFields.Count) proto message(s) with fields."
} else {
    Write-Warning "ei.proto not found at: $protoPath - descriptions will omit field names."
}

function Get-ProtoFieldLines([string]$typeName) {
    if ($protoMessageFields.ContainsKey($typeName)) {
        return $protoMessageFields[$typeName]
    }
    return $null
}

function Build-Description([string]$requestType, [string]$responseType) {
    if ($requestType -eq 'AuthenticatedMessage') {
        $reqBlock = "Request: AuthenticatedMessage`n  The pre-request script fills 'data' from the 'userId' collection variable."
    } else {
        $fields = Get-ProtoFieldLines $requestType
        if ($fields -and $fields.Count -gt 0) {
            $fieldLines = ($fields | ForEach-Object { "  $_" }) -join "`n"
            $reqBlock = "Request: $requestType`n$fieldLines"
        } else {
            $reqBlock = "Request: $requestType"
        }
    }

    $respFields = Get-ProtoFieldLines $responseType
    if ($respFields -and $respFields.Count -gt 0) {
        $respFieldLines = ($respFields | ForEach-Object { "  $_" }) -join "`n"
        $respBlock = "Response: $responseType`n$respFieldLines"
    } else {
        $respBlock = "Response: $responseType"
    }

    return "$reqBlock`n`n$respBlock`n`nSubmit base64-encoded proto bytes as the 'data' form field (pre-request script fills this from the 'userId' collection variable). The response body is also base64-encoded proto."
}

# Pre-request script lines (shared by every POST request)
$preRequestExec = @(
    "const userId = pm.collectionVariables.get('userId') || '';",
    "if (userId) {",
    "    const bytes = [];",
    "    for (let i = 0; i < userId.length; i++) bytes.push(userId.charCodeAt(i));",
    "    // AuthenticatedMessage field 6 (user_id), wire type 2 (length-delimited)",
    "    const proto = [0x32, bytes.length, ...bytes];",
    "    const base64 = btoa(String.fromCharCode(...proto));",
    "    pm.variables.set('authData', base64);",
    "} else {",
    "    pm.variables.set('authData', '');",
    "}"
)

# Group by namespace prefix (part before the first '/')
$groups = $entries | Group-Object { ($_.Path -split '/')[0] }

$folderItems = $groups | Sort-Object Name | ForEach-Object {
    $requests = $_.Group | ForEach-Object {
        $e = $_
        $parts = $e.Path -split '/'
        [ordered]@{
            name = $e.Path
            event = @(
                [ordered]@{
                    listen = 'prerequest'
                    script = [ordered]@{
                        type = 'text/javascript'
                        exec = $preRequestExec
                    }
                }
            )
            request = [ordered]@{
                method = 'POST'
                header = @()
                body = [ordered]@{
                    mode = 'urlencoded'
                    urlencoded = @(
                        [ordered]@{
                            key = 'data'
                            value = '{{authData}}'
                            description = "base64($($e.RequestType) proto bytes)"
                            type = 'text'
                        }
                    )
                }
                url = [ordered]@{
                    raw = "{{baseUrl}}/$($e.Path)"
                    host = @('{{baseUrl}}')
                    path = $parts
                    query = @(
                        [ordered]@{
                            key = 'sim'
                            value = ''
                            description = 'Simulation behavior (server_error, maintenance, not_found, unauthorized, rate_limited, empty, corrupt). Leave blank for normal fixture response.'
                            disabled = $true
                        }
                    )
                }
                description = Build-Description $e.RequestType $e.ResponseType
            }
            response = @()
        }
    }
    [ordered]@{
        name = $_.Name
        item = @($requests)
    }
}

# Simulation folder
$simRequests = @(
    [ordered]@{
        name = 'OPTIONS / (all behaviors)'
        request = [ordered]@{
            method = 'OPTIONS'
            header = @()
            url = [ordered]@{
                raw = '{{baseUrl}}/'
                host = @('{{baseUrl}}')
                path = @('')
            }
            description = 'Returns JSON array of all simulation behaviors. Pass ?sim=<name> on any POST request to trigger a behavior instead of serving a fixture.'
        }
        response = @()
    },
    [ordered]@{
        name = 'OPTIONS /{slug} (behaviors for endpoint)'
        request = [ordered]@{
            method = 'OPTIONS'
            header = @()
            url = [ordered]@{
                raw = '{{baseUrl}}/ei/first_contact_secure'
                host = @('{{baseUrl}}')
                path = @('ei', 'first_contact_secure')
            }
            description = 'Returns JSON array of simulation behaviors applicable to the given endpoint slug. Change the URL path to filter for a different endpoint.'
        }
        response = @()
    }
)

$simFolder = [ordered]@{
    name = 'Simulation'
    item = $simRequests
}

$collection = [ordered]@{
    info = [ordered]@{
        '_postman_id' = 'egg-inc-test-api'
        name = 'EggIncognito'
        description = 'Mock server for the Egg, Inc. API (auxbrain.com). Each request POSTs base64-encoded protobuf bytes as a form field named "data". Responses are also base64-encoded protobuf. Start the server: dotnet run --project EggIncognito'
        schema = 'https://schema.getpostman.com/json/collection/v2.1.0/collection.json'
    }
    variable = @(
        [ordered]@{
            key = 'baseUrl'
            value = 'http://localhost:5080'
            type = 'string'
            description = 'EggIncognito server base URL'
        },
        [ordered]@{
            key = 'userId'
            value = ''
            type = 'string'
            description = 'Your Egg, Inc. EID (e.g. EI1234567890123456). Used by the pre-request script to build AuthenticatedMessage proto bytes.'
        }
    )
    item = @($folderItems) + @($simFolder)
}

$json = $collection | ConvertTo-Json -Depth 20
Set-Content -Path $outPath -Value $json -Encoding UTF8

Write-Host "Collection written to: $outPath"
Write-Host "Import: Postman > File > Import > select the JSON file."
Write-Host "Set the 'baseUrl' and 'userId' collection variables before sending requests."
