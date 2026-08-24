# persistent-composite-storage-delete-toctou-idempotence — Close the delete enumeration race

**row:** `persistent-composite-storage-delete-toctou-idempotence` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/PersistentCompositeStorage.cs`
- `tests/RoslynMcp.Tests/Services/PersistentCompositeStorageTests.cs`

## Acceptance

- [ ] Treat `IOException` from root-directory enumeration after the existence check as an already-deleted/idempotent outcome.
- [ ] Continue to propagate `UnauthorizedAccessException` and other permission/policy failures.
- [ ] Reuse the deterministic filesystem seam without expanding it into a general storage abstraction.
- [ ] One regression injects enumeration `MoveNext` disappearance and one permission case proves the propagation policy.

## Evidence

The deterministic read-race repair exposed the same `Directory.Exists` then lazy `Directory.EnumerateDirectories` TOCTOU window in `Delete`. A sibling process can remove the root between those operations and surface an exception from an otherwise idempotent token deletion.
