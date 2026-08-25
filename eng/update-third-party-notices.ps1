[CmdletBinding()]
param(
    [string]$RepoRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$PackageMetadataRoot,
    [switch]$Verify,
    [switch]$VerifyRestoredLicenses
)

$ErrorActionPreference = 'Stop'

$categoryOrder = @('Runtime Dependencies', 'Build-Time Dependencies', 'Test Dependencies')
$attributions = @{
    'ModelContextProtocol' = @{ Category = 'Runtime Dependencies'; License = 'Apache-2.0'; Project = 'https://github.com/modelcontextprotocol/csharp-sdk' }
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
$script:noticeAssetRecords = $null

function Get-RestoredAssetRecords {
    if ($null -ne $script:noticeAssetRecords) {
        return @($script:noticeAssetRecords)
    }

    $assetsFiles = @(Get-ChildItem -LiteralPath $RepoRoot -Recurse -Filter 'project.assets.json' -File |
        Where-Object { $_.FullName -match '[\\/]obj[\\/]project\.assets\.json\z' })
    if ($assetsFiles.Count -eq 0) {
        throw "No project.assets.json files exist under '$RepoRoot'. Run restore first."
    }

    $records = [System.Collections.Generic.List[object]]::new()
    foreach ($assetsFile in $assetsFiles) {
        $assets = Get-Content -LiteralPath $assetsFile.FullName -Raw | ConvertFrom-Json -Depth 100
        $libraries = [System.Collections.Generic.HashSet[string]]::new(
            [System.StringComparer]::OrdinalIgnoreCase)
        foreach ($library in $assets.libraries.PSObject.Properties.Name) {
            [void]$libraries.Add($library)
        }
        $records.Add([pscustomobject]@{
            Path = $assetsFile.FullName
            Libraries = $libraries
            PackageRoots = @($assets.packageFolders.PSObject.Properties.Name)
        })
    }

    $script:noticeAssetRecords = @($records)
    return @($script:noticeAssetRecords)
}

function Get-RestoredNuspecPath {
    param(
        [Parameter(Mandatory)]
        [string]$PackageId,

        [Parameter(Mandatory)]
        [string]$Version
    )

    function Resolve-FromRoots {
        param([Parameter(Mandatory)][string[]]$Roots)

        foreach ($root in $Roots) {
            $canonicalRoot = if ([System.IO.Path]::IsPathFullyQualified($root)) {
                [System.IO.Path]::GetFullPath($root)
            }
            else {
                [System.IO.Path]::GetFullPath((Join-Path $RepoRoot $root))
            }
            $candidate = Join-Path $canonicalRoot "$($PackageId.ToLowerInvariant())/$($Version.ToLowerInvariant())/$($PackageId.ToLowerInvariant()).nuspec"
            if ([System.IO.File]::Exists($candidate)) {
                return [System.IO.Path]::GetFullPath($candidate)
            }
        }

        return $null
    }

    if (-not [string]::IsNullOrWhiteSpace($PackageMetadataRoot)) {
        $explicitPath = Resolve-FromRoots -Roots @($PackageMetadataRoot)
        if ($null -eq $explicitPath) {
            throw "Unable to read authoritative restored package metadata for $PackageId ${Version}: no exact nuspec exists under '$PackageMetadataRoot'."
        }
        return $explicitPath
    }

    $libraryKey = "$PackageId/$Version"
    $selectedPaths = [System.Collections.Generic.HashSet[string]]::new(
        [System.StringComparer]::OrdinalIgnoreCase)
    foreach ($assetRecord in @(Get-RestoredAssetRecords)) {
        if (-not $assetRecord.Libraries.Contains($libraryKey)) {
            continue
        }

        $selected = Resolve-FromRoots -Roots $assetRecord.PackageRoots
        if ($null -eq $selected) {
            throw "Assets file '$($assetRecord.Path)' restores $libraryKey but none of its effective package roots contains the exact nuspec."
        }
        [void]$selectedPaths.Add($selected)
    }

    if ($selectedPaths.Count -eq 0) {
        throw "Unable to verify restored package metadata for ${libraryKey}: no assets file restores the exact central pin."
    }
    if ($selectedPaths.Count -gt 1) {
        throw "Ambiguous restored package metadata for ${libraryKey}: assets files resolve more than one exact nuspec path: $(@($selectedPaths) -join ', ')."
    }

    return @($selectedPaths)[0]
}

function Read-NuspecDocument {
    param(
        [Parameter(Mandatory)]
        [string]$PackageId,

        [Parameter(Mandatory)]
        [string]$Version
    )

    $nuspecPath = Get-RestoredNuspecPath -PackageId $PackageId -Version $Version
    $sourceDescription = "restored package metadata for $PackageId $Version"
    $content = [System.IO.File]::ReadAllText($nuspecPath)

    $settings = [System.Xml.XmlReaderSettings]::new()
    $settings.DtdProcessing = [System.Xml.DtdProcessing]::Prohibit
    $settings.XmlResolver = $null
    $reader = $null
    $stringReader = $null
    try {
        $stringReader = [System.IO.StringReader]::new($content)
        $reader = [System.Xml.XmlReader]::Create($stringReader, $settings)
        $document = [System.Xml.XmlDocument]::new()
        $document.XmlResolver = $null
        $document.Load($reader)
        return $document
    }
    catch {
        throw "Unable to parse authoritative ${sourceDescription}: $($_.Exception.Message)"
    }
    finally {
        if ($null -ne $reader) {
            $reader.Dispose()
        }
        if ($null -ne $stringReader) {
            $stringReader.Dispose()
        }
    }
}

function Get-NuGetLicenseExpression {
    param(
        [Parameter(Mandatory)]
        [string]$PackageId,

        [Parameter(Mandatory)]
        [string]$Version
    )

    $document = Read-NuspecDocument -PackageId $PackageId -Version $Version
    $metadata = $document.SelectSingleNode("/*[local-name()='package']/*[local-name()='metadata']")
    if ($null -eq $metadata) {
        throw "Authoritative package metadata for '$PackageId $Version' has no package/metadata element."
    }

    $idNode = $metadata.SelectSingleNode("*[local-name()='id']")
    $versionNode = $metadata.SelectSingleNode("*[local-name()='version']")
    if ($null -eq $idNode -or $null -eq $versionNode) {
        throw "Authoritative package metadata for '$PackageId $Version' must declare id and version elements."
    }

    $declaredId = [string]$idNode.InnerText
    $declaredVersion = [string]$versionNode.InnerText
    if ($declaredId -cne $PackageId -or $declaredVersion -cne $Version) {
        throw "Authoritative package metadata identity mismatch: requested '$PackageId $Version' but nuspec declares '$declaredId $declaredVersion'."
    }

    $license = $metadata.SelectSingleNode("*[local-name()='license']")
    if ($null -eq $license -or $license.GetAttribute('type') -cne 'expression' -or [string]::IsNullOrWhiteSpace($license.InnerText)) {
        throw "Authoritative package metadata for '$PackageId $Version' must declare a non-empty SPDX license expression."
    }

    return [System.Text.RegularExpressions.Regex]::Replace($license.InnerText.Trim(), '\s+', ' ')
}

$packagesPath = Join-Path $RepoRoot 'Directory.Packages.props'
$noticePath = Join-Path $RepoRoot 'THIRD-PARTY-NOTICES.md'
[xml]$packagesDocument = Get-Content -LiteralPath $packagesPath -Raw
$packages = @($packagesDocument.Project.ItemGroup.PackageVersion | ForEach-Object {
    [pscustomobject]@{ Id = [string]$_.Include; Version = [string]$_.Version }
})

if ($VerifyRestoredLicenses) {
    foreach ($package in $packages) {
        $attribution = $attributions[$package.Id]
        if ($null -ne $attribution) {
            $declaredLicense = Get-NuGetLicenseExpression -PackageId $package.Id -Version $package.Version
            if ($declaredLicense -cne $attribution.License) {
                throw "Authoritative package metadata for '$($package.Id) $($package.Version)' declares license '$declaredLicense', but reviewed attribution declares '$($attribution.License)'."
            }
        }
    }
}

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
$lines.Add('Roslyn-Backed MCP Server uses the following open-source packages. Versions come from `Directory.Packages.props`; every reviewed license is verified against the exact restored NuGet package metadata.')

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
$lines.Add('Run `pwsh eng/update-third-party-notices.ps1` after changing central package pins. The release gate verifies every reviewed license against restored metadata and fails closed on drift.')
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
