# host-metadata-temp-cleanup-bare-catch — Host-metadata temp-file cleanup swallows every exception in a bare catch

**row:** `host-metadata-temp-cleanup-bare-catch` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Host.Stdio/Diagnostics/HostProcessMetadataStore.cs:151-159`
- `tests/RoslynMcp.Tests/HostProcessMetadataTests.cs`

## Acceptance

- [ ] The temp-file delete in `WriteCurrent`'s move-failure fallback catches only `IOException` and `UnauthorizedAccessException`. Any other exception reaches the method's outer best-effort handler, which already logs at Warning.
- [ ] A caught cleanup failure is logged at Debug through the store's `ILogger` and names the leftover temp path. `WriteCurrent` still completes with the record written.
- [ ] Red-first test in `HostProcessMetadataTests`: force the fallback with a failing temp delete (a file-operation seam, or a Windows-conditional handle on the `.tmp` held without `FileShare.Delete`). Assert that the record is written and that exactly one Debug entry reports the cleanup failure. Today nothing is logged.

## Evidence

- At `716b5e20`, `HostProcessMetadataStore.cs:158` reads `try { File.Delete(tempPath); } catch { /* leave the .tmp; not load-bearing */ }`. That bare catch discards every exception type without logging. It sits inside the `catch (IOException)` fallback (`:155-159`) that runs when `File.Move(tempPath, _filePath, overwrite: true)` (`:153`) fails.
- Every other best-effort handler in the file logs what it swallows: `:161-170` at Warning, `:239-245` and `:252-260` at Debug.
- Found by the cold review of PR #1664 (2026-09-28).

## Context

- Low: this is shutdown-path observability only, and a leftover `.tmp` is harmless. The bare catch still hides unexpected exception types and breaks the repo's rule against silently swallowed exceptions.
- PR #1664 (open at filing) edits this file's comments. Re-derive the line numbers after it lands.
