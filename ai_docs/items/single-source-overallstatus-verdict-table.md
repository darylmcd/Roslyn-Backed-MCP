# single-source-overallstatus-verdict-table — single-source the validate_workspace overallStatus verdict table

**row:** `single-source-overallstatus-verdict-table` · **pri:** `Medium` · **size:** `S`

## Anchors

- `skills/modernize/SKILL.md:121` (Output Format template still enumerates the old 4-value set)
- `skills/mcp-server-surface-test/prompts/phases/apply-and-test.md:154`
- `ai_docs/prompts/stress-test-external-repo.md:175`
- `ai_docs/domains/tool-usage-guide.md:69` (proposed canonical location)
- `src/RoslynMcp.Host.Stdio/Tools/ValidationBundleTools.cs:19`

## Acceptance

- [ ] `tool-usage-guide.md` holds the one canonical verdict table (7 values, reachability-annotated); the two skill files, the two tool Descriptions, and the two prompt surfaces reference it instead of restating caller-action prose.
- [ ] No surface in the repo enumerates a verdict set that stops at `clean | compile-error | analyzer-error | test-failure` — the four sites above are updated or converted to pointers.

## Evidence

- Code-quality review of PR #1169 (`document-git-status-unknown-verdict`): grepped all `overallStatus` enumerations in the repo at HEAD — `modernize/SKILL.md:121`, `apply-and-test.md:154`, and `stress-test-external-repo.md:175` all still list only the original four values, while the shipped diff moved four other surfaces to six/seven values. The `test-zero-run` caller-action sentence is now byte-identical across four files with no shared source.

## Context

Spin-off from the `document-git-status-unknown-verdict` initiative (backlog-sweep plan `20260806T023001Z_backlog-sweep`, PR #1169). The initiative's own Risks field named this drift hazard; this row is the follow-through.
