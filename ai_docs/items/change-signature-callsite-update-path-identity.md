# change-signature-callsite-update-path-identity — Preserve case-sensitive caller file identity

**row:** `change-signature-callsite-update-path-identity` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/ChangeSignatureAddRemovePreviewBuilder.cs:20`
- `tests/RoslynMcp.Tests/ChangeSignaturePreviewTests.cs`

## Acceptance

- [ ] Use filesystem-aware identity when accumulating callsite counts; audit every same-mechanism grouping in this builder.
- [ ] Observe a Linux case-distinct caller regression fail before the fix and pass afterward: two separate source documents produce separate CallsiteUpdateDto entries whose counts match their file mutations.
- [ ] Preserve Windows case-insensitive identity and ordinary multi-callsite aggregation; retain real preview/apply compilation coverage.

## Evidence

- Source verified during October 7 remediation resume at main `6cf842a8b7ea8ac56d7706e9c91c451d1852d9b4`.
- Line 20: `var perFileCallsites = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);`.
- Line 170 increments this dictionary by `filePath`; line 35 projects its entries into callsite updates. Case-distinct paths therefore collapse on Linux.
- BuildFileChangesAsync instead iterates distinct DocumentId entries at lines 200-208, so changed-file entries can remain distinct while counts collapse. No executable regression has been run in this planning session.

## Context

- Different mechanism from primary-constructor binding and safe public refusals. If a selected change directly exposes this grouping, include the fix in that implementation and reconcile this row with verified evidence.
