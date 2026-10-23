# physical-input-identity-cancellation — Refuse cancelled physical identity capture for every input kind

**row:** `physical-input-identity-cancellation` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/WorkspaceRecoveryAuthority.cs`: PhysicalInputIdentity.CaptureAsync.
- `tests/RoslynMcp.Tests/PhysicalInputIdentityCancellationTests.cs`.

## Acceptance

- [ ] Reject an already-cancelled capture for files, directories and missing paths before returning an identity; preserve the exact caller cancellation token.
- [ ] Preserve cancellation checks through physical inspection and fresh file hashing; no successful identity may bypass the caller deadline solely because it has no bytes.
- [ ] Observe directory/missing old behavior fail, retain the file control, and run required fixed-head gates before closure.

## Evidence

| Evidence under `%USERPROFILE%/.codex/scratch/roslyn-prerequisites-20261016-resume` | Result |
|---|---|
| physical-cancellation-causal-ef8fb7100c1242d0bf33c0a58aeb9a66/causal.trx | Guarded old behavior: directory and missing cases fail; file control passes. |
| physical-cancellation-fixed22.trx | All3 cases pass; exact caller token assertions retained. |

## Context

- Exposed by the restore prerequisite's new physical identity helper; fix belongs to this delivery, not a deferred follow-up.
- Source is fixed with focused evidence; keep OPEN pending full/hosted verification and merge. No batch advancement.
