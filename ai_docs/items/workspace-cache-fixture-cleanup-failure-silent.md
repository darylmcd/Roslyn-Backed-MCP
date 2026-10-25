# workspace-cache-fixture-cleanup-failure-silent — Surface cache fixture cleanup failures

**row:** `workspace-cache-fixture-cleanup-failure-silent` · **pri:** `Low` · **size:** `S`

## Anchors

- `tests/RoslynMcp.Tests/Workspace/WorkspaceLoadCacheFastPathTests.cs:64` — private cache deletion through78 discards every cleanup exception.
- `tests/RoslynMcp.Tests/TestInfrastructure/TestFixtureFileSystem.cs` — shared owned-fixture directory cleanup contract.

## Acceptance

- [ ] Reuse shared owned-fixture cleanup and surface unexpected or exhausted cleanup failures.
- [ ] Preserve private-root ownership and manager disposal before deletion; do not broaden deletion or kill unrelated processes.
- [ ] Cover success, missing root and a deterministic terminal cleanup fault; observe the old catch hide the fault before repair.
- [ ] Complete independent review and appropriate qualification before closure.

## Evidence

- Rechecked main58ba4207 source shows Directory.Delete inside a bare catch in the cache fixture. No cleanup-fault regression has run; source-level finding only.
- This is distinct from the production metadata-store cleanup row and other fixture ownership rows.

## Context

- Independent nonblocking follow-up; no restore closure dependency. Intake: `<user>/.codex/scratch/roslyn-remediate-20261023-split/restore-closeout-queued-findings-reconciliation.md`.
