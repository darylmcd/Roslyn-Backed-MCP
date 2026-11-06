# composite-apply-retry-same-token-message — Composite failure message says to retry with the same token after a disk claim consumed it

**row:** `composite-apply-retry-same-token-message` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/CompositeApplyOrchestrator.cs`

## Acceptance

- [ ] The message matches whether the token is still redeemable; a test pins both memory and disk modes.

## Evidence

- CompositeApplyOrchestrator.cs ProjectFailure message; PR 1997 consumes disk tokens on admission refusal.
