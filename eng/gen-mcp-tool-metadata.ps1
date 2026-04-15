#!/usr/bin/env pwsh
<#
.SYNOPSIS
  One-time generator that adds [McpToolMetadata] attributes alongside every
  [McpServerTool] method in src/RoslynMcp.Host.Stdio/Tools/*.cs, deriving the
  metadata from ServerSurfaceCatalog.Tools entries.

.NOTES
  Idempotent: if a method already carries [McpToolMetadata], the file is
  inspected but the existing attribute is preserved (string match on the
  attribute name in the lines immediately following the [McpServerTool] line).

  Run this BEFORE deleting the static Tools list from ServerSurfaceCatalog.cs.
#>
[CmdletBinding()]
param(
  [string]$ToolsDir = "$PSScriptRoot/../src/RoslynMcp.Host.Stdio/Tools",
  [string]$CatalogPath = "$PSScriptRoot/../src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.cs"
)

$ErrorActionPreference = 'Stop'

# 1. Parse catalog rows into a map of toolName -> { Category, Tier, ReadOnly, Destructive, Summary }.
$catalogText = Get-Content -Path $CatalogPath -Raw
$rowRegex = '(?ms)Tool\(\s*"(?<name>[a-z_]+)"\s*,\s*"(?<category>[^"]+)"\s*,\s*"(?<tier>[^"]+)"\s*,\s*(?<readOnly>true|false)\s*,\s*(?<destructive>true|false)\s*,\s*"(?<summary>(?:[^"\\]|\\.)*)"'
$catalog = @{}
foreach ($m in [regex]::Matches($catalogText, $rowRegex)) {
  $name = $m.Groups['name'].Value
  $catalog[$name] = [pscustomobject]@{
    Name        = $name
    Category    = $m.Groups['category'].Value
    Tier        = $m.Groups['tier'].Value
    ReadOnly    = $m.Groups['readOnly'].Value
    Destructive = $m.Groups['destructive'].Value
    # Unescape \" -> " so the emitted attribute literal renders correctly.
    Summary     = $m.Groups['summary'].Value -replace '\\"', '"'
  }
}
Write-Host "Parsed $($catalog.Count) catalog entries from $CatalogPath"

# 2. For each tool file, scan for [McpServerTool(Name = "X", ...)] lines and
#    insert a [McpToolMetadata(...)] attribute immediately after on the same logical
#    attribute group (i.e. before the [Description(...)] entry that typically follows).
$files = Get-ChildItem -Path $ToolsDir -Filter '*.cs' -File
$totalInserted = 0
$totalSkipped = 0

# Re-escape user text into a C# string literal (just doubling quotes for verbatim form
# would be clean, but we keep the regular-string format and escape backslashes + quotes).
function Format-CsString([string]$s) {
  return '"' + ($s -replace '\\', '\\\\' -replace '"', '\"') + '"'
}

# Quote a category/tier identifier for inclusion in the attribute call.
function Format-CsBool([string]$v) { if ($v -eq 'true') { 'true' } else { 'false' } }

foreach ($file in $files) {
  $lines = Get-Content -Path $file.FullName
  $modified = $false
  $needsUsing = $false
  $hasUsing = $false

  $output = New-Object System.Collections.Generic.List[string]
  for ($i = 0; $i -lt $lines.Count; $i++) {
    $line = $lines[$i]
    if ($line -match '^using RoslynMcp\.Host\.Stdio\.Catalog;') { $hasUsing = $true }

    # Match: [McpServerTool(Name = "tool_name", ...
    if ($line -match '^(?<indent>\s*)\[McpServerTool\(Name\s*=\s*"(?<name>[a-z_]+)"') {
      $indent = $Matches['indent']
      $name = $Matches['name']
      if (-not $catalog.ContainsKey($name)) {
        Write-Warning "[$($file.Name)] tool '$name' has no catalog entry — skipping"
        $output.Add($line)
        continue
      }

      # Look ahead within the same attribute group (i.e. lines starting with `,` or
      # newline-continuation up to the first non-attribute line) to detect existing
      # McpToolMetadata.
      $alreadyHas = $false
      for ($j = $i + 1; $j -lt $lines.Count; $j++) {
        $look = $lines[$j].TrimStart()
        if ($look -match '^McpToolMetadata\b' -or $look -match '^\[McpToolMetadata\b') { $alreadyHas = $true; break }
        # Stop scanning when we hit something that isn't part of the same attribute list.
        if ($look -match '^\]' -or $look -match '^public\b' -or $look -match '^internal\b' -or $look -match '^private\b' -or $look -match '^static\b' -or $look -match '^Description\(' -or $look -match '^\[Description\(') { break }
      }
      if ($alreadyHas) {
        $totalSkipped++
        $output.Add($line)
        continue
      }

      # Check if [McpServerTool(...)] line ends with a closing `)],` pattern (single-line)
      # or continues — in either case we insert McpToolMetadata as a new attribute that
      # piggybacks onto the same `[ ... ]` group via a `, McpToolMetadata(...)` insertion.
      $entry = $catalog[$name]
      $attr = "$($indent) McpToolMetadata($((Format-CsString $entry.Category)), $((Format-CsString $entry.Tier)), $((Format-CsBool $entry.ReadOnly)), $((Format-CsBool $entry.Destructive)),`n$($indent)    $((Format-CsString $entry.Summary))),"

      # Find where this attribute group ends. If the McpServerTool attribute spans
      # multiple lines (typical:  [McpServerTool(Name = "...", ReadOnly = true, ...),
      #                            Description("...")]), we want to insert McpToolMetadata
      # between McpServerTool and Description.
      # Strategy: emit the current line, scan forward for the closing `)` of McpServerTool
      # (matching parens), then insert the new attribute line.
      $output.Add($line)

      # Walk forward to the line containing the closing ')' of McpServerTool, tracking
      # paren depth across lines.
      $depth = ([regex]::Matches($line, '\(')).Count - ([regex]::Matches($line, '\)')).Count
      $endIdx = $i
      while ($depth -gt 0 -and ($endIdx + 1) -lt $lines.Count) {
        $endIdx++
        $next = $lines[$endIdx]
        $depth += ([regex]::Matches($next, '\(')).Count - ([regex]::Matches($next, '\)')).Count
        $output.Add($next)
      }

      # The McpServerTool attribute ends at $endIdx. The closing token of its
      # bracket group is either `)]` (group closed; need a fresh `[..]` for our
      # new attribute) or `),` (group continues; we can splice in as a sibling).
      $closingLine = $output[$output.Count - 1]
      $isGroupClosed = $closingLine -match '\)\s*\]\s*$'

      if ($isGroupClosed) {
        # Emit our metadata as its own attribute group on the next line.
        $newAttr = "$indent[McpToolMetadata($((Format-CsString $entry.Category)), $((Format-CsString $entry.Tier)), $((Format-CsBool $entry.ReadOnly)), $((Format-CsBool $entry.Destructive)),`n$indent    $((Format-CsString $entry.Summary)))]"
        $output.Add($newAttr)
      } else {
        # Splice into the existing `[ ... ]` group (ends with `,` continuation).
        $newAttr = "$indent McpToolMetadata($((Format-CsString $entry.Category)), $((Format-CsString $entry.Tier)), $((Format-CsBool $entry.ReadOnly)), $((Format-CsBool $entry.Destructive)),`n$indent    $((Format-CsString $entry.Summary))),"
        $output.Add($newAttr)
      }
      $i = $endIdx
      $modified = $true
      $needsUsing = $true
      $totalInserted++
      continue
    }
    $output.Add($line)
  }

  if ($modified) {
    if ($needsUsing -and -not $hasUsing) {
      # Insert `using RoslynMcp.Host.Stdio.Catalog;` after the last existing using.
      $usingIdx = -1
      for ($k = 0; $k -lt $output.Count; $k++) {
        if ($output[$k] -match '^using\s') { $usingIdx = $k }
      }
      if ($usingIdx -ge 0) {
        $output.Insert($usingIdx + 1, 'using RoslynMcp.Host.Stdio.Catalog;')
      }
    }
    Set-Content -Path $file.FullName -Value $output -NoNewline:$false
    Write-Host "[$($file.Name)] +$($totalInserted) attributes"
  }
}

Write-Host ""
Write-Host "Done. Inserted: $totalInserted, Skipped (already annotated): $totalSkipped"
