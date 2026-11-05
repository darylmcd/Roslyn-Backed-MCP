# analyzer-shadow-resolve-swallows-io — Analyzer shadow dependency resolve turns IOException into a silent null

**row:** `analyzer-shadow-resolve-swallows-io` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Helpers/AnalyzerReferenceIsolation.cs`

## Acceptance

- [ ] An IO failure during resolve is reported, not hidden as a missing dependency; red regression first.

## Evidence

- AnalyzerReferenceIsolation.cs ~1013.
