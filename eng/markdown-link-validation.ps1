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

function Get-MarkdownLinkIssue {
    param([System.IO.FileInfo[]]$Files)

    # Load the bundled parser through its owning module; no download or assembly path probing.
    $null = ConvertFrom-Markdown -InputObject 'Initialize Markdown parser'
    $builder = [Markdig.MarkdownPipelineBuilder]::new()
    $builder.PreciseSourceLocation = $true
    $pipeline = $builder.Build()

    foreach ($file in $Files) {
        $content = Get-Content -LiteralPath $file.FullName -Raw
        $content = Remove-MarkdownCode -Content $content -Pipeline $pipeline
        $matches = [regex]::Matches($content, '\[[^\]]+\]\(([^)]+)\)')

        foreach ($match in $matches) {
            $target = $match.Groups[1].Value.Trim()

            if ($target -match '^(https?:|mailto:|#)') {
                continue
            }

            # Skip template placeholder links like ([#{prNumber}]({prUrl})) — doc authors
            # use these in example blocks; they are not resolvable filesystem paths.
            if ($target -match '\{[^}]*\}') {
                continue
            }

            $pathPart = $target.Split('#')[0].Split('?')[0] -replace '%20', ' '
            if ([string]::IsNullOrWhiteSpace($pathPart)) {
                continue
            }

            # All-dot targets like `(...)` are ellipsis placeholders, never real paths. Windows
            # path normalization makes Test-Path resolve them (so the self-hosted PR runs passed)
            # while Linux treats them as a plain missing filename (so every hosted-ubuntu run
            # failed) — flag them on every platform so PR CI catches what the weekly run would.
            # Intentional placeholder links must use the {braces} convention skipped above.
            if ($pathPart -match '^\.{3,}[/\\]?$') {
                Write-Output ("Broken relative link (all-dot placeholder; use {braces} instead): $($file.FullName) -> $target")
                continue
            }

            if ($pathPart -match '^[a-zA-Z]:\\') {
                if (-not (Test-Path -LiteralPath $pathPart)) {
                    Write-Output ("Broken absolute link: $($file.FullName) -> $target")
                }

                continue
            }

            $resolved = Join-Path -Path $file.DirectoryName -ChildPath $pathPart
            if (-not (Test-Path -LiteralPath $resolved)) {
                Write-Output ("Broken relative link: $($file.FullName) -> $target")
            }
        }
    }
}
