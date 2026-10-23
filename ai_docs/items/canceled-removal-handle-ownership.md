# canceled-removal-handle-ownership

**row:** `canceled-removal-handle-ownership` · **pri:** `High` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Helpers/PhysicalDirectoryLease.cs:431`
- `tests/RoslynMcp.Tests/PhysicalDirectoryRemovalCancellationTests.cs`

## Acceptance

- [ ] Move every fallible operation inside the ownership cleanup boundary for transferred native handles.
- [ ] Observe the Windows pre-cancellation regression fail before the fix and pass after it; verify exact cancellation token and native delete sharing.
- [ ] Audit sibling ownership-taking helpers without weakening physical identity or deletion checks.

## Evidence

- Cold full-diff review found cancellation before the target-owning try; no DELETE-handle release occurs on this branch.
