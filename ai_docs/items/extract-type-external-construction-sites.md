# extract-type-external-construction-sites — Report construction sites broken by the added constructor parameter

**row:** `extract-type-external-construction-sites` · **pri:** `Medium` · **size:** `S` · **deps:** `extract-type-static-source-composition`

## Anchors

- `src/RoslynMcp.Roslyn/Services/TypeExtractionService.cs:899-946` — external-consumer scan covers extracted members only, never source constructors.

## Acceptance

- [ ] Red first: a `new Source()` in another file compiles before the preview and fails after apply.
- [ ] Refuse before token storage with a structured list of the construction sites (file, line), matching the existing blocking-dependency shape.
- [ ] Moves that add no constructor parameter (see `extract-type-static-source-composition`) are unaffected.

## Evidence

- 2026-10-27: every instance extraction adds a required constructor parameter; no scan reports external construction sites.

## Context

- Found during the archive diagnostic; not covered by the archive.
