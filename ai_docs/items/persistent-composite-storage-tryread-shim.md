# persistent-composite-storage-tryread-shim — TryRead is a consuming compat shim used only by a test

**row:** `persistent-composite-storage-tryread-shim` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/PersistentCompositeStorage.cs`
- `tests/RoslynMcp.Tests/Services/PersistentCompositeStorageTests.cs`

## Acceptance

- [ ] TryRead removed; the test uses TryPeek/TryClaim; no behavior change.

## Evidence

- PersistentCompositeStorage.cs ~135; PersistentCompositeStorageTests.cs ~81.
