# ADR 0032 — Persisted composite token workspace identity

- **Status:** Accepted (2026-11-06)
- **Deciders:** repository maintainers
- **Scope:** composite preview tokens persisted under `ROSLYNMCP_PREVIEW_PERSIST_DIR` and their redemption through `apply_composite`
- **Supersedes:** nothing
- **Superseded by:** nothing

## Context

`ROSLYNMCP_PREVIEW_PERSIST_DIR` exists so one `roslynmcp` process can preview and another can apply
(multi-agent orchestration). The persisted record named the creating process's workspace session by
its id, and `WorkspaceManager` mints that id as a random GUID per process. A second host therefore
could never redeem a record:

1. `BoundedStore.PeekWorkspaceId` is memory-only. The receiving host has no memory entry, so the peek
   returned nothing and `apply_composite` refused with a stale-token error before the write gate or
   the claim.
2. Had the peek worked, the stored id names no session of the receiving host. The write gate
   (`WorkspaceExecutionGate`), the orchestrator's session check and the claimed-record admission all
   require a live local session with that exact id.

Only the composite store persists tokens; the other preview stores are memory-only and are unaffected.

## Decision

Persisted records move to format 2 and are bound to a portable identity instead of a session id.

| Item | Rule |
|---|---|
| Identity | The normalized full path of the solution or project the creating session loaded (`WorkspaceIdentity`), compared with the platform path comparer. |
| `FormatVersion` | Absent means 1. This server writes 2. A reader refuses a record above 2 before claiming it (`null` peek, payload kept). |
| `WorkspaceId`, `WorkspaceVersion` | Still written, diagnostics only. The version counts one session's reloads and is never compared across hosts. |
| Resolution | The receiving host lists its sessions and selects those with an equal path. Exactly one selects it. The creating session redeems its own record even when other sessions share the path. Zero or more than one refuses. |
| Format 1 record | Redeems only when its stored session id is live on the receiving host (the creator). Otherwise it is refused as written by an older server. |
| Refusal point | Peek and claim both resolve. The claim resolves again under the claim lock and before the payload is moved, so every refusal leaves the one-time token redeemable. A session change between peek and claim also refuses before the move, because the write gate was taken on the peeked session. |
| Result | `Retrieve` returns the local session id, so the existing gate, orchestrator and write-set admission checks pass unchanged. |
| Persisting | A session with no loaded path cannot persist a record; the preview is refused. |

Refusals are `PublicInvalidOperationException` with a corrective action: load the solution, close the
extra sessions, or redeem on the creating host.

The change is additive. Older readers ignore `FormatVersion` and `WorkspaceIdentity`; they still fail
to redeem across hosts as before, and they redeem on the creating host as before. In-memory mode is
unchanged.

## Consequences

- A host redeems a token only for a solution it has loaded. Path equality is the only equivalence.
- Residual risk: the receiving host may have different on-disk content for the same path than the
  creator had when it previewed. Identity does not prove content equality, so a composite can apply
  to files that drifted. Detecting that is owned by
  [ADR 0033](0033-composite-preview-source-fingerprints.md) (format 3 records carry source
  fingerprints; a format 2 reader still refuses them, so the "additive" claim above holds for format 1
  readers and for readers at format 3 or later, not for a host still running this ADR's release).
- Residual risk (threat note): path identity replaces an unguessable per-process session GUID.
  Anyone with write access to the persist directory can author or edit a record naming any solution
  path, and a host with that solution loaded will accept it. Admission checks the claimed write set
  against the sanctioned roots only (PR #1997); it does not check that the claimed paths lie inside
  the receiving solution's root. Follow-up: solution-root containment of the claimed write set. The
  persist directory therefore stays a trusted location.
- The same solution loaded twice on the receiving host (for example with different MSBuild global
  properties) refuses as ambiguous. The operator closes the extra sessions. The creating host is
  exempt for its own records.
- A format 1 record written before this change redeems only where its creator session is still live.
  Persisted tokens expire after the preview TTL (5 minutes by default), so no migration step exists.
- `ICompositePreviewStore.Retrieve` takes the gated session id, and `ICompositeApplyOrchestrator`
  takes a `CompositeRedemptionAdmission`. Both are internal seams of this server, not tool contracts.
- Rollback: downgrading restores same-host redemption of format 2 records (the old reader ignores the
  new members and matches the stored session id).
