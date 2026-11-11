# extract-type-gate-location-key-unification — Unify the compile-delta location key between extract_type and interface gates

**row:** `extract-type-gate-location-key-unification` · **pri:** `Low` · **size:** `M` · **verified:** `95f651345b37a2849fe02bd026f827813658573f`

## Anchors

- `src/RoslynMcp.Roslyn/Helpers/PreviewCompilationDelta.cs:18-23,44` — `locationKey` parameter of IntroducedErrorsAsync.
- `src/RoslynMcp.Roslyn/Services/TypeExtractionService.cs:908-911` — DocumentKey: document id, else `location.Kind`.
- `src/RoslynMcp.Roslyn/Helpers/InterfacePreviewCompilationGate.cs:25-29` — LocationKey: document id, else `SourceTree.FilePath`, else empty (non-source locations: `Kind`).
- `tests/RoslynMcp.Tests/TypeExtractionTests.cs`

## Acceptance

- [ ] One key rule lives in PreviewCompilationDelta (document id, then source-tree file path, then location kind); the `locationKey` parameter and both private copies are deleted.
- [ ] Red first: a diagnostic in a source tree that has no document id (for example a source-generated tree) is keyed by file path in both gates, so the same pre-existing error is not reported as introduced after a move.
- [ ] Existing extract_type and interface compile-gate tests stay green; no refusal message changes.

## Evidence

- 2026-11-11 re-derivation at 95f65134: the two fallbacks differ. PR #2064 made the key a parameter to avoid changing either caller; the divergence remains.
