# test-fixtures-prepared-flag-fresh-worktree-trap — Document or guard -p:TestFixturesPrepared=true in a fresh worktree

**row:** `test-fixtures-prepared-flag-fresh-worktree-trap` · **pri:** `Low` · **size:** `S`

## Anchors

- `justfile`
- `tests/RoslynMcp.Tests/RoslynMcp.Tests.csproj`
- `ai_docs/runtime.md`

## Acceptance

- [ ] ai_docs/runtime.md names the flag and the fixture-preparation precondition, or the flag fails fast with a clear message when fixtures are absent; verified from a fresh worktree.

## Evidence

- Worker of relocation-support-enum-delegate-move-to-project: 7 interface-route tests failed identically on base in a fresh worktree and pass without the flag; cold review confirmed (RoslynMcp.Tests.csproj:84-88 runs PrepareTestFixtures only when TestFixturesPrepared != true; justfile :39, :43, :82).
