# rename-metadata-refusal-public-message - Preserve metadata rename guidance

**row:** `rename-metadata-refusal-public-message` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/RefactoringService.cs`: public and on-solution metadata-only rename guards.
- `tests/RoslynMcp.Tests/RenameMetadataRefusalTests.cs`
- `tests/RoslynMcp.Tests/TestData/invalid-operation-construction-baseline.json`: remove verified vanished identities only.

## Acceptance

- [ ] Share the metadata rename guard and expose precise source-declaration guidance through the public error contract in both routes.
- [ ] Observe old BCL exception failures, then prove exact public exception and unchanged solution/token authority.
- [ ] Burn down both old ratchet entries; never add a renamed member allowance for migrated code.

## Evidence

- Source review2026-10-16: explicit preview authorization adds owning workspace identity to the on-solution member. Both metadata guards still construct plain InvalidOperationException, losing public guidance and leaving one stale ratchet member identity. Tests-first probes saved; no runtime proof yet. Forced companion of the restore preview authorization migration.
