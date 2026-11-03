# fork-admission-dispose-lock-release — Preserve fork ownership release after cleanup failures

**row:** `fork-admission-dispose-lock-release` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/ForkAdmissionMaterial.cs` (`ForkAdmissionExecution.CreateAsync`, `Dispose`)
- `src/RoslynMcp.Roslyn/Services/WorkspaceForkApplyService.cs` (`AcquireForkApplyLocksAsync`, `ReleaseForkApplyLocks`, `ApplyCoreAsync`, `RestoreForkAsync`, `ForkApplyLockLease.Dispose`)
- focused fork admission and ownership lifecycle tests

## Acceptance

- [ ] Shared fork lock release attempts every owned disposer in reverse acquisition order even when one throws, preserving the primary and later cleanup failures.
- [ ] Admission construction, invocation disposal, acquisition rollback, apply, and restore retain the primary failure while attempting every owned release exactly once. Do not mark uncertain cleanup as complete.
- [ ] Red-first throwing-disposer regressions cover earlier and later ownership, successful and failing operation bodies, and ordinary disposal behavior.

## Evidence

- Cold workspace-disposal plan review on 2026-10-31: `_registration.Dispose()` precedes the `try/finally` that releases fork apply locks, so its exception skips that release.
- Cold fork-admission plan review in the 2026-11-02 remediation: `CreateAsync`'s catch can skip deletion/release after registration disposal, and `ReleaseForkApplyLocks` stops at the first throwing owner. The shared release path also serves acquisition rollback and fork apply/restore. Complete this shared admission/lock slice before dependent `fork-cleanup-dispose-skips-owned-release` handles `CleanupFork` reservations.
