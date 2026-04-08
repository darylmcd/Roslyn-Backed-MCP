param(
    [string]$Configuration = "Release",
    [string]$OutputRoot = "artifacts"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$solutionPath = Join-Path $repoRoot "RoslynMcp.slnx"
$hostProject = Join-Path $repoRoot "src\RoslynMcp.Host.Stdio\RoslynMcp.Host.Stdio.csproj"
$publishDir = Join-Path $repoRoot "$OutputRoot\publish\host-stdio"
$manifestDir = Join-Path $repoRoot "$OutputRoot\manifests"
$coverageDir = Join-Path $repoRoot "$OutputRoot\coverage"
$hashManifestPath = Join-Path $manifestDir "host-stdio-sha256.txt"

New-Item -ItemType Directory -Path $publishDir -Force | Out-Null
New-Item -ItemType Directory -Path $manifestDir -Force | Out-Null
New-Item -ItemType Directory -Path $coverageDir -Force | Out-Null

dotnet restore $solutionPath --nologo
dotnet build $solutionPath -c $Configuration --no-restore --nologo
dotnet test $solutionPath -c $Configuration --no-build --nologo `
    --collect:"XPlat Code Coverage" `
    --results-directory $coverageDir `
    --logger "console;verbosity=normal"
dotnet publish $hostProject -c $Configuration --no-build -o $publishDir

$hashLines = Get-ChildItem -Path $publishDir -File -Recurse |
    Sort-Object FullName |
    ForEach-Object {
        $hash = (Get-FileHash -Path $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        $relativePath = Resolve-Path -Relative $_.FullName
        "$hash  $relativePath"
    }

Set-Content -Path $hashManifestPath -Value $hashLines

Write-Host "Publish directory: $publishDir"
Write-Host "Hash manifest: $hashManifestPath"
Write-Host "Code coverage (Cobertura): $coverageDir"
