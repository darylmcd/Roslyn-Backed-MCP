[CmdletBinding()]
param(
    [string]$RepoRoot = (Split-Path -Parent $PSScriptRoot),
    [switch]$Verify
)

$ErrorActionPreference = 'Stop'

$categoryOrder = @('Runtime Dependencies', 'Build-Time Dependencies', 'Test Dependencies')
$attributions = @{
    'ModelContextProtocol' = @{ Category = 'Runtime Dependencies'; License = 'MIT'; Project = 'https://github.com/modelcontextprotocol/csharp-sdk' }
    'Microsoft.CodeAnalysis.CSharp' = @{ Category = 'Runtime Dependencies'; License = 'MIT'; Project = 'https://github.com/dotnet/roslyn' }
    'Microsoft.CodeAnalysis.CSharp.Workspaces' = @{ Category = 'Runtime Dependencies'; License = 'MIT'; Project = 'https://github.com/dotnet/roslyn' }
    'Microsoft.CodeAnalysis.CSharp.Features' = @{ Category = 'Runtime Dependencies'; License = 'MIT'; Project = 'https://github.com/dotnet/roslyn' }
    'Microsoft.CodeAnalysis.Features' = @{ Category = 'Runtime Dependencies'; License = 'MIT'; Project = 'https://github.com/dotnet/roslyn' }
    'Microsoft.CodeAnalysis.Workspaces.MSBuild' = @{ Category = 'Runtime Dependencies'; License = 'MIT'; Project = 'https://github.com/dotnet/roslyn' }
    'Microsoft.CodeAnalysis.CSharp.Scripting' = @{ Category = 'Runtime Dependencies'; License = 'MIT'; Project = 'https://github.com/dotnet/roslyn' }
    'Microsoft.Build.Locator' = @{ Category = 'Runtime Dependencies'; License = 'MIT'; Project = 'https://github.com/microsoft/MSBuildLocator' }
    'Microsoft.Extensions.Hosting' = @{ Category = 'Runtime Dependencies'; License = 'MIT'; Project = 'https://github.com/dotnet/runtime' }
    'Microsoft.Extensions.Http' = @{ Category = 'Runtime Dependencies'; License = 'MIT'; Project = 'https://github.com/dotnet/runtime' }
    'Microsoft.Extensions.Logging' = @{ Category = 'Runtime Dependencies'; License = 'MIT'; Project = 'https://github.com/dotnet/runtime' }
    'Microsoft.Extensions.Logging.Console' = @{ Category = 'Runtime Dependencies'; License = 'MIT'; Project = 'https://github.com/dotnet/runtime' }
    'DiffPlex' = @{ Category = 'Runtime Dependencies'; License = 'Apache-2.0'; Project = 'https://github.com/mmanela/diffplex' }
    'Nito.AsyncEx' = @{ Category = 'Runtime Dependencies'; License = 'MIT'; Project = 'https://github.com/StephenCleary/AsyncEx' }
    'System.Security.Cryptography.Xml' = @{ Category = 'Runtime Dependencies'; License = 'MIT'; Project = 'https://github.com/dotnet/runtime' }

    'Microsoft.CodeAnalysis.Analyzers' = @{ Category = 'Build-Time Dependencies'; License = 'MIT'; Project = 'https://github.com/dotnet/roslyn-analyzers' }
    'Microsoft.Build' = @{ Category = 'Build-Time Dependencies'; License = 'MIT'; Project = 'https://github.com/dotnet/msbuild' }
    'Microsoft.Build.Framework' = @{ Category = 'Build-Time Dependencies'; License = 'MIT'; Project = 'https://github.com/dotnet/msbuild' }
    'Microsoft.Build.Tasks.Core' = @{ Category = 'Build-Time Dependencies'; License = 'MIT'; Project = 'https://github.com/dotnet/msbuild' }
    'Microsoft.Build.Utilities.Core' = @{ Category = 'Build-Time Dependencies'; License = 'MIT'; Project = 'https://github.com/dotnet/msbuild' }
    'Microsoft.CodeAnalysis.NetAnalyzers' = @{ Category = 'Build-Time Dependencies'; License = 'MIT'; Project = 'https://github.com/dotnet/sdk' }
    'Microsoft.CodeAnalysis.BannedApiAnalyzers' = @{ Category = 'Build-Time Dependencies'; License = 'MIT'; Project = 'https://github.com/dotnet/roslyn-analyzers' }
    'Microsoft.SourceLink.GitHub' = @{ Category = 'Build-Time Dependencies'; License = 'MIT'; Project = 'https://github.com/dotnet/sourcelink' }

    'Microsoft.Extensions.TimeProvider.Testing' = @{ Category = 'Test Dependencies'; License = 'MIT'; Project = 'https://github.com/dotnet/extensions' }
    'Microsoft.NET.Test.Sdk' = @{ Category = 'Test Dependencies'; License = 'MIT'; Project = 'https://github.com/microsoft/vstest' }
    'MSTest.TestAdapter' = @{ Category = 'Test Dependencies'; License = 'MIT'; Project = 'https://github.com/microsoft/testfx' }
    'MSTest.TestFramework' = @{ Category = 'Test Dependencies'; License = 'MIT'; Project = 'https://github.com/microsoft/testfx' }
    'coverlet.collector' = @{ Category = 'Test Dependencies'; License = 'MIT'; Project = 'https://github.com/coverlet-coverage/coverlet' }
    'Microsoft.CodeAnalysis.CSharp.Analyzer.Testing.MSTest' = @{ Category = 'Test Dependencies'; License = 'MIT'; Project = 'https://github.com/dotnet/roslyn-sdk' }
    'NuGet.Frameworks' = @{ Category = 'Test Dependencies'; License = 'Apache-2.0'; Project = 'https://github.com/NuGet/NuGet.Client' }
}

$packagesPath = Join-Path $RepoRoot 'Directory.Packages.props'
$noticePath = Join-Path $RepoRoot 'THIRD-PARTY-NOTICES.md'
[xml]$packagesDocument = Get-Content -LiteralPath $packagesPath -Raw
$packages = @($packagesDocument.Project.ItemGroup.PackageVersion | ForEach-Object {
    [pscustomobject]@{ Id = [string]$_.Include; Version = [string]$_.Version }
})

$unknownPackages = @($packages | Where-Object { -not $attributions.ContainsKey($_.Id) } | ForEach-Object Id | Sort-Object)
$unusedAttributions = @($attributions.Keys | Where-Object { $_ -notin $packages.Id } | Sort-Object)
if ($unknownPackages.Count -gt 0 -or $unusedAttributions.Count -gt 0) {
    $parts = @()
    if ($unknownPackages.Count -gt 0) { $parts += 'unreviewed packages: ' + ($unknownPackages -join ', ') }
    if ($unusedAttributions.Count -gt 0) { $parts += 'stale attribution entries: ' + ($unusedAttributions -join ', ') }
    throw 'Third-party attribution map mismatch: ' + ($parts -join '; ')
}

$lines = [System.Collections.Generic.List[string]]::new()
$lines.Add('# Third-Party Notices')
$lines.Add('')
$lines.Add('Roslyn-Backed MCP Server uses the following open-source packages. Versions come from `Directory.Packages.props`; license and project fields are reviewed attribution metadata maintained by `eng/update-third-party-notices.ps1`.')

foreach ($category in $categoryOrder) {
    $lines.Add('')
    $lines.Add("## $category")
    $lines.Add('')
    $lines.Add('| Package | Version | License | Project |')
    $lines.Add('|---|---:|---|---|')
    foreach ($package in $packages | Where-Object { $attributions[$_.Id].Category -eq $category } | Sort-Object Id) {
        $attribution = $attributions[$package.Id]
        $lines.Add("| $($package.Id) | $($package.Version) | $($attribution.License) | $($attribution.Project) |")
    }
}

$lines.Add('')
$lines.Add('---')
$lines.Add('')
$lines.Add('Run `pwsh eng/update-third-party-notices.ps1` after changing central package pins. Verification fails closed when a package lacks reviewed attribution metadata.')
$lines.Add('')
# Repository markdown uses canonical LF on every runner. Environment.NewLine would make
# verify-only mode disagree across Windows and Linux even when the package inventory matches.
$generated = $lines -join "`n"

if ($Verify) {
    $current = Get-Content -LiteralPath $noticePath -Raw
    if ($current -cne $generated) {
        throw "THIRD-PARTY-NOTICES.md is stale. Run 'pwsh eng/update-third-party-notices.ps1' and review the attribution diff."
    }
    Write-Host 'Third-party notice verification passed.'
    return
}

[System.IO.File]::WriteAllText($noticePath, $generated, [System.Text.UTF8Encoding]::new($false))
Write-Host "Updated $noticePath"
