# change-signature-callsite-summary-stale-row-comments — resolve dangling backlog-row pointer in comments

**row:** `change-signature-callsite-summary-stale-row-comments` · **pri:** `Low` · **size:** `S` <!-- cache — the backlog row is canonical for pri/size; refresh on open if they disagree -->

## Anchors

- `skills/mcp-server-surface-test/prompts/phases/apply-and-test.md`
- `tests/RoslynMcp.Tests/ChangeSignaturePreviewTests.cs`

## Acceptance

- [ ] Decided whether the callsite-summary limitation remains live; either a bounded backlog row restored for it, or the comments rewritten to describe shipped behavior/current limitation without a stale row id
- [ ] Regression: static grep asserts `change-signature-preview-callsite-summary` appears only if the id exists in `ai_docs/backlog.md`

## Evidence

- 2026-06-02 top-5 remediation review, Standing Directive #3.

## Context

Prompt/test comments still reference absent backlog row `change-signature-preview-callsite-summary`, leaving readers with a non-resolving work pointer.
