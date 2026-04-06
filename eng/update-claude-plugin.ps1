#requires -Version 7.0
<#
.SYNOPSIS
    Updates the locally cached roslyn-mcp Claude Code plugin to match the
    latest commit on GitHub main.

.DESCRIPTION
    Performs the equivalent of running these slash commands inside Claude Code:

        /plugin marketplace update roslyn-mcp-marketplace
        /plugin install roslyn-mcp@roslyn-mcp-marketplace

    Use this script when your Claude Code client does not intercept the
    `/plugin` slash commands (some channels and older builds do not), or when
    you want to refresh the plugin from a terminal without opening the REPL.

    The script:
      1. git-pulls the marketplace clone under
         ~/.claude/plugins/marketplaces/roslyn-mcp-marketplace/
      2. Wipes and re-syncs the plugin cache directory
         (~/.claude/plugins/cache/roslyn-mcp-marketplace/roslyn-mcp/<ver>/)
         from the marketplace clone, copying only git-tracked files.
      3. Updates lastUpdated and gitCommitSha in
         ~/.claude/plugins/known_marketplaces.json and installed_plugins.json.

    After this script finishes, restart Claude Code so the new MCP server
    binary, skills, and hooks are loaded.

    This script does NOT rebuild the `roslynmcp` global .NET tool. Run
    `dotnet publish src/RoslynMcp.Host.Stdio -c Release -p:ReinstallTool=true`
    separately for that.

.EXAMPLE
    pwsh ./eng/update-claude-plugin.ps1

.EXAMPLE
    pwsh ./eng/update-claude-plugin.ps1 -PluginVersion 1.6.0
#>

[CmdletBinding()]
param(
    [string] $MarketplaceName = 'roslyn-mcp-marketplace',
    [string] $PluginName = 'roslyn-mcp',
    [string] $PluginVersion,
    [string] $ClaudeHome = (Join-Path $HOME '.claude')
)

$ErrorActionPreference = 'Stop'

function Write-Step([string] $Message) {
    Write-Host "==> $Message" -ForegroundColor Cyan
}

$pluginsDir = Join-Path $ClaudeHome 'plugins'
$marketplaceDir = Join-Path $pluginsDir "marketplaces/$MarketplaceName"
$knownMarketplacesPath = Join-Path $pluginsDir 'known_marketplaces.json'
$installedPluginsPath = Join-Path $pluginsDir 'installed_plugins.json'

if (-not (Test-Path $marketplaceDir)) {
    throw "Marketplace clone not found at $marketplaceDir. Install the plugin from Claude Code at least once before running this script."
}
if (-not (Test-Path $knownMarketplacesPath)) {
    throw "known_marketplaces.json not found at $knownMarketplacesPath."
}
if (-not (Test-Path $installedPluginsPath)) {
    throw "installed_plugins.json not found at $installedPluginsPath."
}

# 1. Pull the marketplace clone.
Write-Step "Pulling marketplace clone in $marketplaceDir"
Push-Location $marketplaceDir
try {
    git fetch origin | Out-Null
    git checkout main | Out-Null
    git pull --ff-only origin main | Out-Null
    $headSha = (git rev-parse HEAD).Trim()
    Write-Host "    HEAD is now $headSha"
}
finally {
    Pop-Location
}

# 2. Resolve plugin version + cache dir.
$installed = Get-Content $installedPluginsPath -Raw | ConvertFrom-Json
$installKey = "$PluginName@$MarketplaceName"
$installEntries = $installed.plugins.$installKey
if (-not $installEntries) {
    throw "No installed plugin entry found for '$installKey' in installed_plugins.json. Install via Claude Code first."
}
$installEntry = $installEntries[0]

if (-not $PluginVersion) {
    $PluginVersion = $installEntry.version
}
$cacheDir = Join-Path $pluginsDir "cache/$MarketplaceName/$PluginName/$PluginVersion"
Write-Host "    Plugin cache target: $cacheDir"

# 3. Re-sync the plugin cache from the marketplace clone (git-tracked files only).
Write-Step "Re-syncing plugin cache from marketplace clone"
if (Test-Path $cacheDir) {
    Remove-Item $cacheDir -Recurse -Force
}
New-Item -ItemType Directory -Path $cacheDir | Out-Null

Push-Location $marketplaceDir
try {
    $trackedFiles = git ls-files
    foreach ($relPath in $trackedFiles) {
        $src = Join-Path $marketplaceDir $relPath
        $dst = Join-Path $cacheDir $relPath
        $dstDir = Split-Path $dst -Parent
        if (-not (Test-Path $dstDir)) {
            New-Item -ItemType Directory -Path $dstDir -Force | Out-Null
        }
        Copy-Item $src $dst -Force
    }
    Write-Host "    Copied $($trackedFiles.Count) files"
}
finally {
    Pop-Location
}

# 4. Update metadata.
Write-Step "Updating plugin metadata"
$nowIso = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ")

$known = Get-Content $knownMarketplacesPath -Raw | ConvertFrom-Json
$known.$MarketplaceName.lastUpdated = $nowIso
($known | ConvertTo-Json -Depth 10) | Set-Content -NoNewline -Encoding UTF8 $knownMarketplacesPath

$installEntry.lastUpdated = $nowIso
$installEntry.gitCommitSha = $headSha
($installed | ConvertTo-Json -Depth 10) | Set-Content -NoNewline -Encoding UTF8 $installedPluginsPath

Write-Host ""
Write-Host "Plugin '$PluginName' updated to commit $headSha." -ForegroundColor Green
Write-Host "Restart Claude Code to pick up the new skills, hooks, and MCP server binary." -ForegroundColor Yellow
