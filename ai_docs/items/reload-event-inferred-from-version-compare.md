# reload-event-inferred-from-version-compare — Report swap commit explicitly instead of inferring it from the version number

**row:** `reload-event-inferred-from-version-compare` · **pri:** `Low` · **size:** `S` · **verified:** `a2a51ec4de033bfd3aa36ba9d238b73e849b0f9a`

## Anchors

- `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs`
- `tests/RoslynMcp.Tests/WorkspaceLoadRegistrationCompensationTests.cs`

## Acceptance

- [ ] ReloadAsync raises WorkspaceReloaded after a post-swap throw and never after a pre-swap failure, independent of concurrent version bumps (deterministic test with an apply landing between the version read and a pre-swap load failure).

## Evidence

- PR 2049 cold review: ReloadAsync compares session.Version before/after the load; TryApplyChanges calls IncrementVersion without LoadLock, so an apply between the read and a pre-swap failure fires a spurious event (one extra cache invalidation, exception still propagates).
