# reserved-row-remediation-route-guidance — Correct active Reserved-row remediation guidance

**row:** `reserved-row-remediation-route-guidance` · **pri:** `Low` · **size:** `S`

## Anchors

- `ai_docs/backlog.md`
- `eng/verify-ai-docs.ps1`

## Acceptance

- [ ] Replace the active `/backlog-sweep:plan` Reserved-row instruction with the canonical `/backlog-remediate` route and its `reserved` skip reason; preserve contributor-reservation and reclaim semantics.
- [ ] Extend AI-doc validation to reject retired executable backlog command references in active workflow contracts without rejecting explicitly historical changelog or archive evidence.

## Evidence

- `ai_docs/backlog.md:32` still directs agents to a retired `/backlog-sweep:plan` Step 1 contract, while the canonical remediation rules now classify Reserved rows under skip reason `reserved`.
