# markdown-link-destination-parser — markdown-link-destination-parser

**row:** `markdown-link-destination-parser` · **pri:** `Low` · **size:** `S`

# markdown-link-destination-parser

## Anchors

- `eng/markdown-link-validation.ps1` — Get-MarkdownLinkIssue target extraction.
- `tests/RoslynMcp.Tests/Skills/DocumentationGateScriptTests.cs` — Markdown link regressions.

## Acceptance

- Extract genuine link destinations from the bundled Markdown parser rather than the remaining bracket/parenthesis regex.
- Preserve existing placeholder, fragment, and missing-path policy while recognizing balanced parentheses in valid filenames.
- Add one fixture with an existing parenthesized target and a missing counterpart; accept the former and reject the latter without truncating either path.

## Evidence

2026-09-15 isolated live probe: a file named a(b).md exists, but the preserved legacy target regex reports a broken target a(b. Code exclusion is repaired separately; this follow-up owns actual destination grammar and path policy.
