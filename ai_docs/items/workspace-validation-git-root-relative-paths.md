# workspace-validation-git-root-relative-paths — workspace-validation-git-root-relative-paths

**row:** `workspace-validation-git-root-relative-paths` · **pri:** `Medium` · **size:** `M`

# workspace-validation-git-root-relative-paths

## Anchors

- `src/RoslynMcp.Roslyn/Services/WorkspaceValidationService.cs` — ParseGitPorcelainZ combines Git paths with the solution directory.
- `src/RoslynMcp.Roslyn/Services/GitChangedFilesCollector.cs` — CollectAsync invokes porcelain-v1 from the solution directory.
- `tests/RoslynMcp.Tests/ValidateRecentGitChangesTests.cs` — isolated Git workspace regression.

## Acceptance

- Resolve porcelain-v1 paths against the repository root, preserving ambient-Git override isolation.
- For a solution under a repository subdirectory, report actual changed source paths without duplicating the subdirectory.
- Add one nested-solution regression with changes inside and outside the solution directory; preserve existing root-level cases.

## Evidence

2026-09-15 isolated Git probe: `git -C <root>/nested status --porcelain=v1 -z -uall` returns `?? nested/Probe.cs` for `<root>/nested/Probe.cs`. The current parser combines this with `<root>/nested`, yielding the nonexistent `<root>/nested/nested/Probe.cs`. This path-resolution defect is independent of process teardown.
