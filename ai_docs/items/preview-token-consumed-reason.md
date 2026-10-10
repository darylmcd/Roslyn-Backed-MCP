# preview-token-consumed-reason — Distinguish applied tokens from reload

**row:** `preview-token-consumed-reason` · **pri:** `Medium` · **size:** `M` · **deps:** `persistent-composite-workspace-identity-not-portable`

## Anchors

- `src/RoslynMcp.Core/Services/BoundedStore.cs:49-52`
- `src/RoslynMcp.Core/Services/PreviewTokenStaleException.cs`
- `src/RoslynMcp.Host.Stdio/Tools/ToolErrorHandler.cs:159-166`
- `tests/RoslynMcp.Tests/PreviewTokenStaleAcrossAutoReloadTests.cs`

## Acceptance

- [ ] Reapplying a consumed preview token reports that it was already applied, without echoing token contents.
- [ ] A genuinely reloaded or expired token remains distinguishable and uses the existing safe error category contract.
- [ ] Red-first tests compare second apply, reload, and TTL expiry through the public tool envelope.

## Evidence

- Parent `preview-token-store-mismatch-false-stale`: `BoundedStore.Invalidate` discards consumed state and `ToolErrorHandler` labels all missing tokens as a workspace reload.

## Context

- Shared lifecycle mechanism across token apply paths; probe all `Invalidate` callers before implementation.

- Source-only independence re-vet at `683dc0fcb503d02f49063e6909e327272ab4044c` disproves inherited five-stage independence: generic Invalidate=>Discarded lies after successful solution/project/composite apply and immediately after persistent claim, before mutation. Query/projection cannot precede all owned completion producers and persistent claim authority.
- Held lifecycle boundary widened through sanctioned stanza-amend: 12 production, 15 tests/assets, 3 docs, genuine estimate160000. Installed exec-args reproduced native exit4 for Rule5 cap80000; existing global filing darylmcd/claude-config#724 owns audited indivisible admission. No implementation or validation pass is claimed.
- `persistent-composite-workspace-identity-not-portable` is an explicit prerequisite: creator UUID/session-local version cannot identify the correct receiving session/snapshot; disk peek alone is insufficient. After it lands, re-vet complete lifecycle scope, test representation dependencies and estimate against the new base and obtain fresh cold review. Preserve all five child identities and existing parent-row closure ownership until actual deliverables land; do not obsolete children or close this row from a planning proposal.
