# fork-expiry-cross-process-active-work — Verify atomic peer admission versus fork expiry

**row:** `fork-expiry-cross-process-active-work` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/WorkspaceForkApplyService.cs:460` — expiry sweep through511; process-local ownership488 and persistent execution check499; persistent guards626–650.
- `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs` — existing source deletion reservation and retained operation authority.
- `tests/RoslynMcp.Tests/Workspace/WorkspaceForkSourceSessionTests.cs`

## Acceptance

- [ ] Reproduce or disprove peer admission racing expiry with two owned processes and explicit barriers. Close the premise if existing protection already makes it safe.
- [ ] If confirmed, coordinate admission, active/uncertain peer work and expiry atomically while preserving existing persistent execution markers, same-host ownership and source ancestry.
- [ ] Cover ordinary idle expiry, active work, crash, cancellation and uncertain ownership; retain material until actual owners finish.
- [ ] Re-derive the smallest existing coordination seam; do not prescribe a new lease/controller framework from an unproven race. Complete review and appropriate qualification before closure.

## Evidence

- Source-level investigation only: main58ba4207 uses process-local sweep ownership then checks persistent work before deletion reservation; peer admission atomicity is not proven by the inspected code.
- Existing HasPendingAdmissionExecutions DOES protect pending directories, verified descendants and uncertain material. The earlier blanket claim that peer work is unprotected is false.
- No causal two-process proof has run or been inspected for this race.

## Context

- Independent nonblocking investigation; no restore closure dependency. Reference intake: `<user>/.codex/scratch/roslyn-remediate-20261023-split/restore-closeout-queued-findings-reconciliation.md`.
