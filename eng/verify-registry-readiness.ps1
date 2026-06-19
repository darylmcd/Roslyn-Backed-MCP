#!/usr/bin/env pwsh
<#
.SYNOPSIS
  Validates .claude-plugin/server.json against MCP registry expectations and
  emits a structured install-readiness artifact for /publish-preflight.

.DESCRIPTION
  First slice of the registry-install-readiness scorecard (BRAIN-002 / backlog
  row registry-install-readiness-scorecard). Performs structural and
  cross-reference checks on the MCP registry manifest plus the Claude Code
  plugin manifest and marketplace descriptor, without contacting any network.

  Checks performed:
    - server.json structural fields (name, description, repository, version,
      packages[], _meta)
    - description within the registry schema's 100-char maxLength
    - packed NuGet README (src/RoslynMcp.Host.Stdio/README.md) carries the
      `<!-- mcp-name: <server.name> -->` ownership marker the registry requires
    - name format `io.github.<owner>/<repo>` matches repository.url
    - packages[0] shape: registryType, registryBaseUrl, identifier, version,
      runtimeHint, transport.type
    - packages[0].identifier matches src/RoslynMcp.Host.Stdio NuGet PackageId
    - packages[0].version matches Directory.Build.props canonical Version
    - server.json $schema URL is set (registry submission requires it)
    - plugin.json round-trip: required fields, version match
    - marketplace.json round-trip: required fields, plugins[0].version match

  Emits a JSON artifact at -OutputPath (default artifacts/registry-readiness.json)
  with a schemaVersion-1 envelope listing every check, its status (pass/fail/warn),
  and a summary verdict (ready | not-ready). /publish-preflight reads this
  artifact and surfaces the verdict as a non-blocking advisory step.

  Exit codes:
    0  No FAIL-level findings (WARN-only is still a pass).
    1  At least one FAIL-level finding.

.PARAMETER OutputPath
  Where to write the structured JSON artifact. Defaults to
  artifacts/registry-readiness.json under the repo root.

.PARAMETER Quiet
  Suppress per-check console output; still emit the artifact and exit code.
#>
[CmdletBinding()]
param(
    [string]$OutputPath,
    [switch]$Quiet
)

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (-not $OutputPath) {
    $OutputPath = Join-Path $repoRoot 'artifacts' 'registry-readiness.json'
}

$checks = [System.Collections.Generic.List[object]]::new()

function Add-Check {
    param(
        [Parameter(Mandatory)] [string]$Id,
        [Parameter(Mandatory)] [ValidateSet('pass', 'fail', 'warn')] [string]$Status,
        [Parameter(Mandatory)] [string]$Message
    )
    $checks.Add([pscustomobject]@{
        id      = $Id
        status  = $Status
        message = $Message
    })
    if (-not $Quiet) {
        $color = switch ($Status) {
            'pass' { 'Green' }
            'fail' { 'Red' }
            'warn' { 'Yellow' }
        }
        $glyph = switch ($Status) {
            'pass' { '[OK]  ' }
            'fail' { '[FAIL]' }
            'warn' { '[WARN]' }
        }
        Write-Host "$glyph $Id - $Message" -ForegroundColor $color
    }
}

function Read-JsonFile {
    param([string]$Path)
    if (-not (Test-Path $Path)) { return $null }
    return Get-Content $Path -Raw | ConvertFrom-Json
}

# --- Load all source files ---
$serverJsonPath = Join-Path $repoRoot '.claude-plugin' 'server.json'
$pluginJsonPath = Join-Path $repoRoot '.claude-plugin' 'plugin.json'
$marketplaceJsonPath = Join-Path $repoRoot '.claude-plugin' 'marketplace.json'
$propsPath = Join-Path $repoRoot 'Directory.Build.props'
$hostProjectPath = Join-Path $repoRoot 'src' 'RoslynMcp.Host.Stdio' 'RoslynMcp.Host.Stdio.csproj'

if (-not (Test-Path $serverJsonPath)) {
    Add-Check -Id 'server-json-exists' -Status 'fail' -Message ".claude-plugin/server.json missing"
    $serverJson = $null
} else {
    Add-Check -Id 'server-json-exists' -Status 'pass' -Message ".claude-plugin/server.json present"
    $serverJson = Read-JsonFile $serverJsonPath
}

$pluginJson = Read-JsonFile $pluginJsonPath
$marketplaceJson = Read-JsonFile $marketplaceJsonPath

[xml]$props = Get-Content $propsPath
$canonicalVersion = $props.Project.PropertyGroup.Version

[xml]$hostProject = Get-Content $hostProjectPath
$nugetPackageId = $hostProject.Project.PropertyGroup |
    Where-Object { $_.PackageId } |
    Select-Object -ExpandProperty PackageId -First 1

# --- server.json structural checks ---
if ($serverJson) {
    $schemaUrl = $serverJson.'$schema'
    if ($schemaUrl) {
        Add-Check -Id 'schema-url' -Status 'pass' -Message "`$schema present: $schemaUrl"
    } else {
        Add-Check -Id 'schema-url' -Status 'fail' -Message '$schema URL missing - MCP registry submission requires it'
    }

    if ($serverJson.name) {
        if ($serverJson.name -match '^io\.github\.[a-zA-Z0-9-]+/[a-zA-Z0-9._-]+$') {
            Add-Check -Id 'name-format' -Status 'pass' -Message "name follows io.github.<owner>/<repo> convention: $($serverJson.name)"
        } else {
            Add-Check -Id 'name-format' -Status 'fail' -Message "name does not match io.github.<owner>/<repo>: '$($serverJson.name)'"
        }
    } else {
        Add-Check -Id 'name-format' -Status 'fail' -Message 'name missing'
    }

    if ($serverJson.description) {
        Add-Check -Id 'description-present' -Status 'pass' -Message "description present ($($serverJson.description.Length) chars)"
        # The registry schema caps description at 100 chars; mcp-publisher rejects longer.
        if ($serverJson.description.Length -le 100) {
            Add-Check -Id 'description-length' -Status 'pass' -Message "description within 100-char schema limit ($($serverJson.description.Length) chars)"
        } else {
            Add-Check -Id 'description-length' -Status 'fail' -Message "description is $($serverJson.description.Length) chars; MCP registry schema maxLength is 100 - trim it"
        }
    } else {
        Add-Check -Id 'description-present' -Status 'fail' -Message 'description missing'
    }

    if ($serverJson.repository -and $serverJson.repository.url -and $serverJson.repository.source) {
        Add-Check -Id 'repository-block' -Status 'pass' -Message "repository.url=$($serverJson.repository.url), source=$($serverJson.repository.source)"

        if ($serverJson.name -match '^io\.github\.([a-zA-Z0-9-]+)/([a-zA-Z0-9._-]+)$') {
            $nameOwner = $Matches[1]
            # Only the OWNER segment must match repository.url's owner; the repo segment of the name
            # is intentionally allowed to differ. GitHub auth grants the whole io.github.<owner>/*
            # namespace, so io.github.darylmcd/roslyn-mcp against repo Roslyn-Backed-MCP is valid,
            # not suspect. Only a wrong OWNER is suspicious.
            if ($serverJson.repository.url -match '^https://github\.com/([a-zA-Z0-9-]+)/') {
                $urlOwner = $Matches[1]
                if ($nameOwner -eq $urlOwner) {
                    Add-Check -Id 'repository-url-matches-name' -Status 'pass' -Message "name owner '$nameOwner' matches repository.url owner '$urlOwner' (repo segment may differ within the io.github.$nameOwner/* namespace)"
                } else {
                    Add-Check -Id 'repository-url-matches-name' -Status 'warn' -Message "name owner '$nameOwner' does not match repository.url owner '$urlOwner'"
                }
            } else {
                Add-Check -Id 'repository-url-matches-name' -Status 'warn' -Message "repository.url '$($serverJson.repository.url)' is not a parseable github.com URL"
            }
        }
    } else {
        Add-Check -Id 'repository-block' -Status 'fail' -Message 'repository.url or repository.source missing'
    }

    if ($serverJson.version) {
        if ($serverJson.version -match '^\d+\.\d+\.\d+(-[a-zA-Z0-9.-]+)?(\+[a-zA-Z0-9.-]+)?$') {
            Add-Check -Id 'version-semver' -Status 'pass' -Message "version is semver: $($serverJson.version)"
        } else {
            Add-Check -Id 'version-semver' -Status 'fail' -Message "version not semver: '$($serverJson.version)'"
        }
        if ($serverJson.version -eq $canonicalVersion) {
            Add-Check -Id 'version-matches-canonical' -Status 'pass' -Message "server.json version matches Directory.Build.props ($canonicalVersion)"
        } else {
            Add-Check -Id 'version-matches-canonical' -Status 'fail' -Message "server.json version '$($serverJson.version)' != canonical '$canonicalVersion'"
        }
    } else {
        Add-Check -Id 'version-semver' -Status 'fail' -Message 'version missing'
    }

    $pkg = $null
    if ($serverJson.packages -and $serverJson.packages.Count -ge 1) {
        $pkg = $serverJson.packages[0]
        Add-Check -Id 'packages-present' -Status 'pass' -Message "packages[0] present (registryType=$($pkg.registryType))"
    } else {
        Add-Check -Id 'packages-present' -Status 'fail' -Message 'packages[] empty or missing'
    }

    if ($pkg) {
        $required = @('registryType', 'registryBaseUrl', 'identifier', 'version', 'runtimeHint')
        $missing = $required | Where-Object { -not $pkg.$_ }
        if ($missing.Count -eq 0) {
            Add-Check -Id 'package-required-fields' -Status 'pass' -Message "packages[0] has all required fields"
        } else {
            Add-Check -Id 'package-required-fields' -Status 'fail' -Message "packages[0] missing: $($missing -join ', ')"
        }

        if ($pkg.transport -and $pkg.transport.type) {
            Add-Check -Id 'package-transport' -Status 'pass' -Message "packages[0].transport.type=$($pkg.transport.type)"
        } else {
            Add-Check -Id 'package-transport' -Status 'fail' -Message 'packages[0].transport.type missing'
        }

        if ($pkg.identifier -eq $nugetPackageId) {
            Add-Check -Id 'package-identifier-matches-nuget' -Status 'pass' -Message "packages[0].identifier matches NuGet PackageId ($nugetPackageId)"
        } else {
            Add-Check -Id 'package-identifier-matches-nuget' -Status 'fail' -Message "packages[0].identifier '$($pkg.identifier)' != NuGet PackageId '$nugetPackageId'"
        }

        if ($pkg.version -eq $canonicalVersion) {
            Add-Check -Id 'package-version-matches-canonical' -Status 'pass' -Message "packages[0].version matches canonical ($canonicalVersion)"
        } else {
            Add-Check -Id 'package-version-matches-canonical' -Status 'fail' -Message "packages[0].version '$($pkg.version)' != canonical '$canonicalVersion'"
        }
    }

    if ($serverJson._meta) {
        Add-Check -Id 'meta-present' -Status 'pass' -Message '_meta block present'
    } else {
        Add-Check -Id 'meta-present' -Status 'warn' -Message '_meta block missing (optional but conventional)'
    }

    # The registry verifies NuGet ownership by fetching the PACKED README from
    # NuGet.org and requiring an `<!-- mcp-name: <server.name> -->` marker. The
    # packed README is the project-local one (PackageReadmeFile), not the repo root.
    $packedReadmePath = Join-Path $repoRoot 'src' 'RoslynMcp.Host.Stdio' 'README.md'
    if (-not (Test-Path $packedReadmePath)) {
        Add-Check -Id 'nuget-readme-mcp-name-marker' -Status 'fail' -Message "packed README not found at src/RoslynMcp.Host.Stdio/README.md"
    } else {
        $expectedMarker = "<!-- mcp-name: $($serverJson.name) -->"
        $readmeText = Get-Content $packedReadmePath -Raw
        if ($readmeText -match [regex]::Escape($expectedMarker)) {
            Add-Check -Id 'nuget-readme-mcp-name-marker' -Status 'pass' -Message "packed README carries ownership marker '$expectedMarker'"
        } else {
            Add-Check -Id 'nuget-readme-mcp-name-marker' -Status 'fail' -Message "packed README (src/RoslynMcp.Host.Stdio/README.md) is missing the registry ownership marker '$expectedMarker'"
        }
    }
}

# --- plugin.json round-trip ---
if (-not $pluginJson) {
    Add-Check -Id 'plugin-json-exists' -Status 'fail' -Message '.claude-plugin/plugin.json missing'
} else {
    Add-Check -Id 'plugin-json-exists' -Status 'pass' -Message '.claude-plugin/plugin.json present'
    foreach ($field in @('name', 'version', 'description', 'repository', 'license')) {
        if ($pluginJson.$field) {
            Add-Check -Id "plugin-$field" -Status 'pass' -Message "plugin.json.$field present"
        } else {
            Add-Check -Id "plugin-$field" -Status 'fail' -Message "plugin.json.$field missing"
        }
    }
    if ($pluginJson.version -eq $canonicalVersion) {
        Add-Check -Id 'plugin-version-matches-canonical' -Status 'pass' -Message "plugin.json.version matches canonical"
    } else {
        Add-Check -Id 'plugin-version-matches-canonical' -Status 'fail' -Message "plugin.json.version '$($pluginJson.version)' != canonical '$canonicalVersion'"
    }
    if ($pluginJson.skills) {
        $skillsDir = Join-Path $repoRoot ($pluginJson.skills -replace '^\./', '')
        if (Test-Path $skillsDir) {
            Add-Check -Id 'plugin-skills-dir-exists' -Status 'pass' -Message "plugin.json.skills resolves to $skillsDir"
        } else {
            Add-Check -Id 'plugin-skills-dir-exists' -Status 'fail' -Message "plugin.json.skills points to non-existent path: $skillsDir"
        }
    }
}

# --- marketplace.json round-trip ---
if (-not $marketplaceJson) {
    Add-Check -Id 'marketplace-json-exists' -Status 'fail' -Message '.claude-plugin/marketplace.json missing'
} else {
    Add-Check -Id 'marketplace-json-exists' -Status 'pass' -Message '.claude-plugin/marketplace.json present'
    $mpPlugin = $marketplaceJson.plugins | Select-Object -First 1
    if ($mpPlugin) {
        if ($mpPlugin.version -eq $canonicalVersion) {
            Add-Check -Id 'marketplace-plugin-version' -Status 'pass' -Message 'marketplace.plugins[0].version matches canonical'
        } else {
            Add-Check -Id 'marketplace-plugin-version' -Status 'fail' -Message "marketplace.plugins[0].version '$($mpPlugin.version)' != canonical '$canonicalVersion'"
        }
        if ($pluginJson -and $mpPlugin.name -eq $pluginJson.name) {
            Add-Check -Id 'marketplace-plugin-name-matches' -Status 'pass' -Message 'marketplace.plugins[0].name matches plugin.json.name'
        } else {
            Add-Check -Id 'marketplace-plugin-name-matches' -Status 'fail' -Message "marketplace.plugins[0].name does not match plugin.json.name"
        }
    } else {
        Add-Check -Id 'marketplace-has-plugin-entry' -Status 'fail' -Message 'marketplace.json plugins[] empty'
    }
}

# --- Emit artifact and summary ---
$pass = ($checks | Where-Object status -EQ 'pass').Count
$failCount = ($checks | Where-Object status -EQ 'fail').Count
$warn = ($checks | Where-Object status -EQ 'warn').Count
$overall = if ($failCount -eq 0) { 'ready' } else { 'not-ready' }

$artifact = [pscustomobject]@{
    schemaVersion  = 1
    generatedAt    = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
    serverJsonPath = (Resolve-Path -Relative $serverJsonPath -ErrorAction SilentlyContinue) ?? $serverJsonPath
    checks         = $checks
    summary        = [pscustomobject]@{
        pass    = $pass
        fail    = $failCount
        warn    = $warn
        overall = $overall
    }
}

$outputDir = Split-Path -Parent $OutputPath
if (-not (Test-Path $outputDir)) {
    New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
}
$artifact | ConvertTo-Json -Depth 6 | Set-Content -Path $OutputPath -Encoding utf8

if (-not $Quiet) {
    Write-Host ''
    Write-Host "Registry readiness: $overall ($pass pass / $failCount fail / $warn warn)"
    Write-Host "Artifact written to: $OutputPath"
}

if ($failCount -gt 0) {
    exit 1
}
exit 0
