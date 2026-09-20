# PowerShell 7 ships Markdig with ConvertFrom-Markdown. Use its source spans to mask code,
# including nested/tilde fences and variable-length inline delimiters, without changing offsets.
function Remove-MarkdownCode {
    param([AllowEmptyString()][string]$Content, $Pipeline)

    $document = [Markdig.Markdown]::Parse($Content, $Pipeline, $null)
    $characters = $Content.ToCharArray()
    foreach ($node in [Markdig.Syntax.MarkdownObjectExtensions]::Descendants($document)) {
        if ($node -is [Markdig.Syntax.CodeBlock] -or $node -is [Markdig.Syntax.Inlines.CodeInline]) {
            $end = $node.Span.End
            # An unclosed fence can retain only the opening-fence span. Its parsed content
            # lines still carry exact source offsets, including a final line without a newline.
            if ($node -is [Markdig.Syntax.CodeBlock]) {
                foreach ($line in $node.Lines) {
                    $end = [Math]::Max($end, $line.Slice.End)
                }
            }
            for ($index = $node.Span.Start; $index -le $end; $index++) {
                if ($characters[$index] -ne [char]13 -and $characters[$index] -ne [char]10) {
                    $characters[$index] = [char]' '
                }
            }
        }
    }
    return -join $characters
}

function ConvertTo-GitHubHeadingSlug {
    param([AllowEmptyString()][string]$Heading)

    # GitHub preserves Unicode letters, combining marks, numbers, underscores,
    # hyphens, and spaces; punctuation, symbols (including emoji), and control
    # characters are removed. Spaces are then replaced one-for-one.
    $slug = $Heading.ToLowerInvariant()
    $slug = [regex]::Replace($slug, '[^\p{L}\p{M}\p{N}_\- ]', '')
    return $slug.Replace(' ', '-')
}

function Get-MarkdownHeadingText {
    param([Markdig.Syntax.HeadingBlock]$Heading)

    $text = [System.Text.StringBuilder]::new()
    foreach ($inline in [Markdig.Syntax.MarkdownObjectExtensions]::Descendants($Heading.Inline)) {
        if ($inline -is [Markdig.Syntax.Inlines.LiteralInline]) {
            $null = $text.Append($inline.Content.ToString())
        }
        elseif ($inline -is [Markdig.Syntax.Inlines.CodeInline]) {
            $null = $text.Append($inline.Content)
        }
        elseif ($inline -is [Markdig.Syntax.Inlines.HtmlEntityInline]) {
            $null = $text.Append($inline.Transcoded.ToString())
        }
        elseif ($inline -is [Markdig.Syntax.Inlines.LineBreakInline]) {
            $null = $text.Append(' ')
        }
        elseif ($inline -is [Markdig.Syntax.Inlines.AutolinkInline]) {
            $null = $text.Append($inline.Url)
        }
    }

    return $text.ToString()
}

function Get-MarkdownAnchorSet {
    param(
        [AllowEmptyString()][string]$Content,
        $Pipeline
    )

    $document = [Markdig.Markdown]::Parse($Content, $Pipeline, $null)
    $slugs = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    $nextSuffix = [System.Collections.Generic.Dictionary[string, int]]::new([System.StringComparer]::Ordinal)

    foreach ($heading in [Markdig.Syntax.MarkdownObjectExtensions]::Descendants($document)) {
        if ($heading -is [Markdig.Syntax.HeadingBlock]) {
            $baseSlug = ConvertTo-GitHubHeadingSlug -Heading (Get-MarkdownHeadingText -Heading $heading)
            $slug = $baseSlug
            while (-not $slugs.Add($slug)) {
                if (-not $nextSuffix.ContainsKey($baseSlug)) {
                    $nextSuffix[$baseSlug] = 0
                }

                $nextSuffix[$baseSlug]++
                $slug = "$baseSlug-$($nextSuffix[$baseSlug])"
            }
        }
        elseif ($heading -is [Markdig.Syntax.Inlines.HtmlInline]) {
            $anchorMatch = [regex]::Match($heading.Tag, '(?i)^<a\s+[^>]*(?:id|name)\s*=\s*["'']([^"'']+)["'']')
            if ($anchorMatch.Success) {
                $null = $slugs.Add([System.Net.WebUtility]::HtmlDecode($anchorMatch.Groups[1].Value))
            }
        }
    }

    return ,$slugs
}

function Get-MarkdownLinkIssueCore {
    param(
        [System.IO.FileInfo[]]$Files,
        $Pipeline
    )

    $pathComparer = if ($IsWindows) { [System.StringComparer]::OrdinalIgnoreCase } else { [System.StringComparer]::Ordinal }
    $headingSlugCache = [System.Collections.Generic.Dictionary[string, object]]::new($pathComparer)

    foreach ($file in $Files) {
        $content = Get-Content -LiteralPath $file.FullName -Raw
        $content = Remove-MarkdownCode -Content $content -Pipeline $pipeline
        $matches = [regex]::Matches($content, '\[[^\]]+\]\(([^)]+)\)')

        foreach ($match in $matches) {
            $target = $match.Groups[1].Value.Trim()

            if ($target -match '^(https?:|mailto:)') {
                continue
            }

            # Skip template placeholder links like ([#{prNumber}]({prUrl})) — doc authors
            # use these in example blocks; they are not resolvable filesystem paths.
            if ($target -match '\{[^}]*\}') {
                continue
            }

            $fragmentIndex = $target.IndexOf('#')
            $fragment = if ($fragmentIndex -ge 0) { $target.Substring($fragmentIndex + 1) } else { $null }
            $pathAndQuery = if ($fragmentIndex -ge 0) { $target.Substring(0, $fragmentIndex) } else { $target }
            $pathPart = $pathAndQuery.Split('?')[0] -replace '%20', ' '

            # All-dot targets like `(...)` are ellipsis placeholders, never real paths. Windows
            # path normalization makes Test-Path resolve them (so the self-hosted PR runs passed)
            # while Linux treats them as a plain missing filename (so every hosted-ubuntu run
            # failed) — flag them on every platform so PR CI catches what the weekly run would.
            # Intentional placeholder links must use the {braces} convention skipped above.
            if (-not [string]::IsNullOrWhiteSpace($pathPart) -and $pathPart -match '^\.{3,}[/\\]?$') {
                Write-Output ("Broken relative link (all-dot placeholder; use {braces} instead): $($file.FullName) -> $target")
                continue
            }

            if (-not [string]::IsNullOrWhiteSpace($pathPart) -and $pathPart -match '^[a-zA-Z]:[\\/]') {
                if (-not (Test-Path -LiteralPath $pathPart)) {
                    Write-Output ("Broken absolute link: $($file.FullName) -> $target")
                    continue
                }

                $resolved = [System.IO.Path]::GetFullPath($pathPart)
            }
            elseif ([string]::IsNullOrWhiteSpace($pathPart)) {
                $resolved = $file.FullName
            }
            else {
                $resolved = [System.IO.Path]::GetFullPath((Join-Path -Path $file.DirectoryName -ChildPath $pathPart))
                if (-not (Test-Path -LiteralPath $resolved)) {
                    Write-Output ("Broken relative link: $($file.FullName) -> $target")
                    continue
                }
            }

            if ($null -eq $fragment -or [string]::IsNullOrWhiteSpace($fragment)) {
                continue
            }

            $extension = [System.IO.Path]::GetExtension($resolved)
            if ($extension -notin @('.md', '.markdown')) {
                continue
            }

            try {
                $decodedFragment = [System.Uri]::UnescapeDataString($fragment)
            }
            catch {
                Write-Output ("Broken Markdown heading fragment: $($file.FullName) -> $target (invalid percent encoding)")
                continue
            }

            if (-not $headingSlugCache.ContainsKey($resolved)) {
                $targetContent = [System.IO.File]::ReadAllText($resolved)
                $headingSlugCache[$resolved] = Get-MarkdownAnchorSet -Content $targetContent -Pipeline $pipeline
            }

            if (-not $headingSlugCache[$resolved].Contains($decodedFragment)) {
                Write-Output ("Broken Markdown heading fragment: $($file.FullName) -> $target")
            }
        }
    }
}

function Get-MarkdownLinkIssue {
    param([System.IO.FileInfo[]]$Files)

    # Load the bundled parser through its owning module; no download or assembly path probing.
    $null = ConvertFrom-Markdown -InputObject 'Initialize Markdown parser'
    $builder = [Markdig.MarkdownPipelineBuilder]::new()
    $builder.PreciseSourceLocation = $true
    $pipeline = $builder.Build()

    $fixtureRoot = Join-Path ([System.IO.Path]::GetTempPath()) "roslynmcp-markdown-link-$([guid]::NewGuid().ToString('N'))"
    try {
        $null = New-Item -ItemType Directory -Path $fixtureRoot
        $targetPath = Join-Path $fixtureRoot 'target.md'
        $validPath = Join-Path $fixtureRoot 'valid.md'
        $deadPath = Join-Path $fixtureRoot 'dead.md'
        $assetPath = Join-Path $fixtureRoot 'asset.txt'
        [System.IO.File]::WriteAllText($targetPath, "# Hello, World!`n`n## Repeated heading`n`n## Repeated heading`n`n## Encoded café`n`n<a id=`"explicit-anchor`"></a>`n")
        [System.IO.File]::WriteAllText($assetPath, 'not Markdown')
        [System.IO.File]::WriteAllText($validPath, "# Local heading`n`n[punctuation](target.md#hello-world)`n[duplicate](target.md#repeated-heading-1)`n[encoded](target.md#encoded-caf%C3%A9)`n[explicit](target.md#explicit-anchor)`n[same file](#local-heading)`n[fragmentless](target.md)`n[non-Markdown](asset.txt#ignored)`n")
        [System.IO.File]::WriteAllText($deadPath, "[dead](target.md#repeated-heading-2)`n")

        $validFixtureIssues = @(Get-MarkdownLinkIssueCore -Files ([System.IO.FileInfo]::new($validPath)) -Pipeline $pipeline)
        if ($validFixtureIssues.Count -ne 0) {
            Write-Output 'Markdown heading-fragment validator rejects its bounded valid fixture.'
        }

        $deadFixtureIssues = @(Get-MarkdownLinkIssueCore -Files ([System.IO.FileInfo]::new($deadPath)) -Pipeline $pipeline)
        if ($deadFixtureIssues.Count -ne 1 -or $deadFixtureIssues[0] -notlike 'Broken Markdown heading fragment:*') {
            Write-Output 'Markdown heading-fragment validator does not reject its bounded dead-fragment fixture.'
        }
    }
    finally {
        if ([System.IO.Directory]::Exists($fixtureRoot)) {
            Remove-Item -LiteralPath $fixtureRoot -Recurse -Force
        }
    }

    Get-MarkdownLinkIssueCore -Files $Files -Pipeline $pipeline
}
