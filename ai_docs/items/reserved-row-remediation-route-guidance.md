# reserved-row-remediation-route-guidance — Correct active Reserved-row remediation guidance

**row:** `reserved-row-remediation-route-guidance` · **pri:** `Low` · **size:** `M`

## Anchors

- `ai_docs/backlog.md`
- `eng/verify-ai-docs.ps1`

## Acceptance

- [ ] Replace the active `/backlog-sweep:plan` Reserved-row instruction with the canonical `/backlog-remediate` route and its `reserved` skip reason; preserve contributor-reservation and reclaim semantics.
- [ ] Extend AI-doc validation to reject retired executable backlog command references in active workflow contracts without rejecting explicitly historical changelog or archive evidence.

## Evidence

- `ai_docs/backlog.md:32` still directs agents to a retired `/backlog-sweep:plan` Step 1 contract, while the canonical remediation rules now classify Reserved rows under skip reason `reserved`.

## Scope amendment — 2026-09-08

### Additional anchor

- `ai_docs/bootstrap-read-tool-primer.md`

### Additional acceptance

- Replace the three active “backlog-sweep subagents” workflow references at lines 24, 68, and 116 with role-neutral or canonical backlog-remediate language.
- Extend the active-versus-history/compatibility inventory regression to cover the primer while allowing dated history and compatibility filenames.

This amendment keeps the row at M: `ai_docs/backlog.md`, `ai_docs/bootstrap-read-tool-primer.md`, `eng/verify-ai-docs.ps1`, and the row-detail deletion.
