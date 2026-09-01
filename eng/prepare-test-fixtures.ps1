[CmdletBinding()]
param(
    [string]$RepoRoot = (Split-Path -Parent $PSScriptRoot)
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$resolvedRepoRoot = (Resolve-Path -LiteralPath $RepoRoot -ErrorAction Stop).Path
$samplesRoot = Join-Path $resolvedRepoRoot 'samples'
if (-not (Test-Path -LiteralPath $samplesRoot -PathType Container)) {
    throw "Test fixture root does not exist: $samplesRoot"
}

$solutions = @(Get-ChildItem -LiteralPath $samplesRoot -Recurse -File -Filter '*.slnx' |
    Sort-Object FullName)
if ($solutions.Count -eq 0) {
    throw "No owned sample solutions were found beneath $samplesRoot"
}

foreach ($solution in $solutions) {
    $relativePath = [System.IO.Path]::GetRelativePath($resolvedRepoRoot, $solution.FullName)
    Write-Host "Preparing test fixture: $relativePath"
    $global:LASTEXITCODE = 0
    & dotnet restore $solution.FullName --nologo
    if ($LASTEXITCODE -ne 0) {
        throw "Test fixture restore failed for '$relativePath' with exit code $LASTEXITCODE."
    }
}

Write-Host "Prepared $($solutions.Count) owned sample solution(s)."
