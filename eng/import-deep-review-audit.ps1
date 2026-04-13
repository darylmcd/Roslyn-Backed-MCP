param(
    [Parameter(Mandatory = $true)]
    [string[]]$AuditFiles,

    [switch]$Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-RepoRoot {
    Split-Path -Parent $PSScriptRoot
}

function Resolve-InputPath {
    param([string]$Path)

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return [System.IO.Path]::GetFullPath($Path)
    }

    return [System.IO.Path]::GetFullPath((Join-Path (Get-Location) $Path))
}

function Test-AuditFileName {
    param([string]$FileName)

    return $FileName -match '^\d{8}T\d{6}Z_.+_(mcp-server-audit|experimental-promotion)\.md$'
}

function Get-RelativePathFromRepo {
    param(
        [string]$BaseDirectory,
        [string]$TargetPath
    )

    # Path.GetRelativePath requires .NET Core 2.1+; Windows PowerShell 5.1 lacks it.
    $baseFull = [System.IO.Path]::GetFullPath($BaseDirectory).TrimEnd('\')
    if (-not $baseFull.EndsWith([string][System.IO.Path]::DirectorySeparatorChar)) {
        $baseFull += [System.IO.Path]::DirectorySeparatorChar
    }
    $targetFull = [System.IO.Path]::GetFullPath($TargetPath)
    $baseUri = New-Object System.Uri($baseFull)
    $targetUri = New-Object System.Uri($targetFull)
    $rel = $baseUri.MakeRelativeUri($targetUri).ToString()
    return ([System.Uri]::UnescapeDataString($rel) -replace '\\', '/')
}

function Test-AuditFileContent {
    param([string]$Path)

    $firstLines = Get-Content -Path $Path -TotalCount 40
    foreach ($line in $firstLines) {
        if ($line -match '^\s*#\s+MCP\s+(Server\s+Audit(\s+Report)?|server\s+audit)\b') {
            return $true
        }
        if ($line -match '^\s*#\s+Experimental\s+Promotion\s+Exercise\s+Report\b') {
            return $true
        }
    }

    return $false
}

$repoRoot = Get-RepoRoot
$destinationDirectory = Join-Path $repoRoot 'ai_docs/audit-reports'

if (-not (Test-Path $destinationDirectory)) {
    New-Item -ItemType Directory -Path $destinationDirectory -Force | Out-Null
}

foreach ($auditFile in $AuditFiles) {
    $sourcePath = Resolve-InputPath -Path $auditFile

    if (-not (Test-Path $sourcePath)) {
        throw "Audit file not found: $auditFile"
    }

    $fileName = [System.IO.Path]::GetFileName($sourcePath)
    if (-not (Test-AuditFileName -FileName $fileName)) {
        throw "Audit file does not match canonical naming: $fileName"
    }

    if (-not (Test-AuditFileContent -Path $sourcePath)) {
        throw "Audit file does not appear to be a deep-review raw report: $sourcePath"
    }

    $destinationPath = Join-Path $destinationDirectory $fileName
    $sourceFullPath = [System.IO.Path]::GetFullPath($sourcePath)
    $destinationFullPath = [System.IO.Path]::GetFullPath($destinationPath)

    if ([string]::Equals($sourceFullPath, $destinationFullPath, [System.StringComparison]::OrdinalIgnoreCase)) {
        Write-Host "Already canonical: $destinationPath"
        continue
    }

    if ((Test-Path $destinationPath) -and -not $Force) {
        throw "Destination already exists: $destinationPath. Use -Force to overwrite."
    }

    Copy-Item -Path $sourcePath -Destination $destinationPath -Force:$Force
    $relativeDestination = Get-RelativePathFromRepo -BaseDirectory $repoRoot -TargetPath $destinationPath
    Write-Host "Imported audit: $relativeDestination"
}