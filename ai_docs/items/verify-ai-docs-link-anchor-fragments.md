# verify-ai-docs-link-anchor-fragments — Validate `#anchor` fragments in ai_docs relative links

**row:** `verify-ai-docs-link-anchor-fragments` · **pri:** `Medium` · **size:** `S`

## Anchors

- `eng/verify-ai-docs.ps1` — relative-link checker resolves the file half of a `[label](path#anchor)` link but never validates the `#anchor` fragment against the target file's headings.

## Acceptance

- A markdown link whose target file exists but whose `#fragment` matches no GitHub-slugged heading in that file is reported as an error by the AI-docs verifier.
- Fragment-less links and links to non-markdown targets are unaffected (no new findings on a clean tree).
- Regression: an in-script fixture pair (valid fragment passes, dead fragment fails) exercised by the normal verifier run — no test project covers the engineering PowerShell scripts today, so the check must self-prove.

## Evidence

`ai_docs/prompts/backlog-sweep-addenda.md` carried `../runtime.md#bootstrap-scope--self-edit-on-this-repository` while `ai_docs/runtime.md` has no such heading (its headings are `## Roslyn MCP Client Policy (AI sessions)` / `### Write-side by session shape`). `./eng/verify-ai-docs.ps1` exited 0 on that tree — "AI docs validation passed" — so the dead anchor survived until a manual 2026-09-15 addenda retrospective found it. File existence is checked; the fragment is not.
