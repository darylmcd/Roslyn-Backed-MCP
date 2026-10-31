# fork-admission-dispose-lock-release — Release fork locks after registration disposal failure

**row:** `fork-admission-dispose-lock-release` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/ForkAdmissionMaterial.cs:264`
- `tests/RoslynMcp.Tests/WorkspaceLoadRegistrationCompensationTests.cs` (or a focused fork-admission lifecycle test)

## Acceptance

- [ ] `ForkAdmissionExecution.Dispose` attempts lock release when registration disposal throws, preserving the first failure and any later cleanup failure.
- [ ] A red-first throwing-registration regression observes each owned lock released exactly once; ordinary disposal behavior remains intact.

## Evidence

- Cold workspace-disposal plan review on 2026-10-31: `_registration.Dispose()` precedes the `try/finally` that releases fork apply locks, so its exception skips that release.
