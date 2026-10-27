[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$RepoRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$resolvedRepoRoot = (Resolve-Path -LiteralPath $RepoRoot -ErrorAction Stop).Path
$solutionPath = Join-Path $resolvedRepoRoot 'RoslynMcp.slnx'
if (-not (Test-Path -LiteralPath $solutionPath -PathType Leaf)) {
    throw "Validation solution does not exist: $solutionPath"
}

$global:LASTEXITCODE = 0
& dotnet restore $solutionPath --nologo
if ($LASTEXITCODE -ne 0) {
    throw "Validation dependency restore failed for the main solution with exit code $LASTEXITCODE."
}

$global:LASTEXITCODE = 0
& (Join-Path $PSScriptRoot 'prepare-test-fixtures.ps1') -RepoRoot $resolvedRepoRoot
if ($LASTEXITCODE -ne 0) {
    throw "Test fixture preparation failed with exit code $LASTEXITCODE."
}
