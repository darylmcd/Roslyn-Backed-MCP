# workspace-cache-fixture-cleanup-failure-silent — Surface cache fixture cleanup failures

**row:** `workspace-cache-fixture-cleanup-failure-silent` · **pri:** `Low` · **size:** `S`

## Anchors

- `tests/RoslynMcp.Tests/Workspace/WorkspaceLoadCacheFastPathTests.cs:64` — private cache deletion through78 discards every cleanup exception.
- `tests/RoslynMcp.Tests/MsBuildEvaluationCacheTests.cs:162` — ProjectFixture.Dispose discards directory-deletion failures through a bare catch.
- `tests/RoslynMcp.Tests/ProjectOutputTypeTests.cs:211` — ProjectFixture.Dispose repeats the same silent directory cleanup.

## Acceptance

- [ ] Reuse shared owned-fixture cleanup and surface unexpected or exhausted cleanup failures.
- [ ] Preserve private-root ownership and manager disposal before deletion; do not broaden deletion or kill unrelated processes.
- [ ] Cover success, missing root and a deterministic terminal cleanup fault; observe the old catch hide the fault before repair.
- [ ] Complete independent review and appropriate qualification before closure.

## Evidence

- Rechecked main58ba4207 source shows Directory.Delete inside a bare catch in the cache fixture. No cleanup-fault regression has run; source-level finding only.
- Reverified on e17fae0d during the 2026-10-25 metadata collection ownership review: MsBuildEvaluationCacheTests and ProjectOutputTypeTests repeat the same bare-catch teardown. They remain unchanged by that implementation; its new lifetime fixture uses shared failure-collecting cleanup. No deterministic cleanup-fault test has run for these old fixtures.
- This is distinct from production metadata collection ownership and metadata-store cleanup.

## Context

- Edit targets are the three fixture files above. Reuse `tests/RoslynMcp.Tests/TestInfrastructure/TestFixtureFileSystem.cs` without changing its shared contract.

- Independent nonblocking follow-up; no restore closure dependency. Intake: `<user>/.codex/scratch/roslyn-remediate-20261023-split/restore-closeout-queued-findings-reconciliation.md`.
