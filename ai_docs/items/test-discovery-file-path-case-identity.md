# test-discovery-file-path-case-identity — Preserve file identity on case-sensitive volumes

**row:** `test-discovery-file-path-case-identity` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/TestDiscoveryService.cs:277-300`
- `src/RoslynMcp.Roslyn/Services/TestDiscoveryService.cs:724-738`
- `src/RoslynMcp.Roslyn/Services/TestDiscoveryService.cs:894-924`

## Acceptance

- [ ] File-path sets and exact file-path comparisons use platform file identity. Preserve case-insensitive search terms and display-name matching.
- [ ] On a case-sensitive volume, two loaded files whose names differ only by case remain distinct in related-test discovery and direct-reference selection.
- [ ] On Windows, path casing variations for one file still select the same tests.

## Evidence

- Code inspection 2026-09-29: file-path sets at lines 277, 733, and 894 use `StringComparer.OrdinalIgnoreCase`; related-file de-duplication at line 724 uses `StringComparer.OrdinalIgnoreCase`. These merge distinct file paths on Linux even though the workspace path helper already exposes platform-specific comparison.

## Context

- Found while correcting junction alias selection in `FindDocumentByPath`. That caller-path lookup now uses platform identity; these related-test collections still collapse distinct paths on case-sensitive volumes.

2026-10-09 planning-quality finding: this initiative performance cell contained copied alias-resolution text belonging to workspace-project-alias-lookup. Corrected through audited stanza-amend; source approach, runtime scope, acceptance and estimate unchanged. This existing row tracks implementation; no runtime completion claimed.
