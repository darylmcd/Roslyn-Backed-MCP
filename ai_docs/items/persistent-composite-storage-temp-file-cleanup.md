# persistent-composite-storage-temp-file-cleanup — Remove failed atomic-write temp files

**row:** `persistent-composite-storage-temp-file-cleanup` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/PersistentCompositeStorage.cs`
- `tests/RoslynMcp.Tests/Services/PersistentCompositeStorageTests.cs`

## Acceptance

- [ ] `Write` removes `{token}.json.tmp` when serialization, write, or final move fails without masking the primary exception.
- [ ] Successful writes remain atomic and leave no temp file.
- [ ] A deterministic regression forces the final move to fail and proves the temp file is removed.

## Evidence

- Cold review on 2026-07-26 found the atomic write path has no cleanup path after `File.WriteAllText` creates the temp file.
