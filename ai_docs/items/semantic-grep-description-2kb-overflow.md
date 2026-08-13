# semantic-grep-description-2kb-overflow — Trim semantic_grep description under the 2KB client cap

**row:** `semantic-grep-description-2kb-overflow` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/AnalysisTools.cs:430` (semantic_grep `[Description]` — 2,244 chars, largest on the surface)
- `tests/RoslynMcp.Tests/` (new/extended surface test: every tool method Description ≤2,000 chars)

## Acceptance

- [ ] semantic_grep method Description ≤2,000 chars with trigger/usage essentials retained; overflow guidance relocated (ServerInstructions / prompts / catalog resource)
- [ ] Surface test asserts every registered tool's method Description ≤2,000 chars (only semantic_grep violates today, so the cap test goes green with this one fix and pins the ceiling)

## Evidence

- Claude Code truncates tool descriptions at 2KB; semantic_grep is 2,244 chars source / 2,745 chars wire — content past the cap is invisible dead weight today — see `ai_docs/reports/20260813T025903Z_roslyn-backed-mcp_mcp-token-overhead-and-conformance-audit.md` §1

## Context

Deliberately split out of `method-description-diet` (an L sweep) because this is a live defect in the dominant client, shippable as a single-file S row now.
