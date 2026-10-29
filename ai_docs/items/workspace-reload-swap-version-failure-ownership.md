# workspace-reload-swap-version-failure-ownership — Fix reload ownership when IncrementVersion/InvalidateOnVersionBump throws after the workspace swap

**row:** `workspace-reload-swap-version-failure-ownership` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs` (LoadIntoSessionAsync, between session.Workspace = newWorkspace and swapped = true)
- `tests/RoslynMcp.Tests/WorkspaceLoadRegistrationCompensationTests.cs`

## Acceptance

- [ ] A throw after the swap but before swapped=true disposes the retired resources, never the live ones; red-first test via a throwing preview-store invalidation.

## Evidence

- workspace-native-load-cleanup-gate-release cold review: pre-existing ownership gap in the same try.
