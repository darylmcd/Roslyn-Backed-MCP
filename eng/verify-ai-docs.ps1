param(
    [string]$RepoRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = "Stop"

$stalePatterns = @(
    'agent_session_instructions.md',
    'editor_notes.md',
    'open_and_deferred_items.md',
    'ai_docs/tmp',
    'ai_docs/quickstart.md'
)

$staleSearchExtensions = @('*.md', '*.ps1', '*.yml', '*.yaml', '*.json')
$contentFiles = Get-ChildItem -Path $RepoRoot -Recurse -File -Include $staleSearchExtensions |
    Where-Object { $_.FullName -ne $PSCommandPath }
$markdownFiles = Get-ChildItem -Path $RepoRoot -Recurse -File -Filter *.md

$issues = New-Object System.Collections.Generic.List[string]

foreach ($pattern in $stalePatterns) {
    $matches = Select-String -Path $contentFiles.FullName -Pattern $pattern -SimpleMatch
    foreach ($match in $matches) {
        $issues.Add("Stale reference: $($match.Path):$($match.LineNumber) -> $pattern")
    }
}

foreach ($file in $markdownFiles) {
    $content = Get-Content -LiteralPath $file.FullName -Raw
    $matches = [regex]::Matches($content, '\[[^\]]+\]\(([^)]+)\)')

    foreach ($match in $matches) {
        $target = $match.Groups[1].Value.Trim()

        if ($target -match '^(https?:|mailto:|#)') {
            continue
        }

        $pathPart = $target.Split('#')[0].Split('?')[0] -replace '%20', ' '
        if ([string]::IsNullOrWhiteSpace($pathPart)) {
            continue
        }

        if ($pathPart -match '^[a-zA-Z]:\\') {
            if (-not (Test-Path -LiteralPath $pathPart)) {
                $issues.Add("Broken absolute link: $($file.FullName) -> $target")
            }

            continue
        }

        $resolved = Join-Path -Path $file.DirectoryName -ChildPath $pathPart
        if (-not (Test-Path -LiteralPath $resolved)) {
            $issues.Add("Broken relative link: $($file.FullName) -> $target")
        }
    }
}

if ($issues.Count -gt 0) {
    $issues | Sort-Object -Unique | ForEach-Object { Write-Error $_ }
    exit 1
}

Write-Host "AI docs validation passed."