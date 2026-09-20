| Field | Content |
|---|---|
| Route | direct |
| Diagnosis | `eng/verify-ai-docs.ps1` delegates markdown links to `eng/markdown-link-validation.ps1`; that helper strips a target's fragment and checks only filesystem existence. The row's implementation anchor is therefore stale, but its regression remains live in the delegated validator. |
| Approach | - A markdown link whose target file exists but whose `#fragment` matches no GitHub-slugged heading in that file is reported as an error by the AI-docs verifier.<br>- Fragment-less links and links to non-markdown targets are unaffected (no new findings on a clean tree).<br>- Regression: an in-script fixture pair (valid fragment passes, dead fragment fails) exercised by the normal verifier run — no test project covers the engineering PowerShell scripts today, so the check must self-prove. |
| Scope | Production script: `eng/markdown-link-validation.ps1`. Documentation/changelog: `changelog.d/verify-ai-docs-link-anchor-fragments.md`. Keep `eng/verify-ai-docs.ps1` unchanged unless the self-proof cannot remain encapsulated in its sourced helper. |
| Tool policy | edit-only |
| Estimated context cost | 18000 |
| Risks | GitHub heading slugs require lowercase normalization, punctuation removal, whitespace-to-hyphen conversion, and duplicate-heading suffixes; percent-decoding and same-file fragments must remain bounded and deterministic. Ignore fragments on non-Markdown targets as required. No refactor fanout applies. |
| Validation | Run the helper's in-script valid/dead fixture proof through `pwsh -NoProfile -File ./eng/verify-ai-docs.ps1`; require the clean repository to pass, then exercise a temporary dead-fragment fixture outside the repository if needed. Run `pwsh -NoProfile -File ./eng/verify-changelog-fragments.ps1` and `just ci`. |
| Performance review | Bound heading extraction to each linked Markdown target and cache computed slug sets per resolved path so repository validation does not repeatedly parse the same file. |
| CHANGELOG category | Fixed |
| CHANGELOG entry (draft) | Validate Markdown heading fragments in relative AI-documentation links. |
| Backlog sync | Close rows: [verify-ai-docs-link-anchor-fragments]. |
