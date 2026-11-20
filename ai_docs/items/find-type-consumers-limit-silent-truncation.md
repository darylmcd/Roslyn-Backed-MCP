# find-type-consumers-limit-silent-truncation — Signal truncation in find_type_consumers

**row:** `find-type-consumers-limit-silent-truncation` · **pri:** `Medium` · **size:** `S` · **verified:** `02fddfa9500d30848b8f5e34367322db1011b3df`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/SymbolTools.cs:695`

## Acceptance

- [ ] `find_type_consumers(typeName=TypeExtractionService, limit=2)` reports the total (5) and hasMore, in the same envelope find_references uses.
- [ ] Additive response fields only; check docs/release-policy.md (experimental tool).
- [ ] Same change considered for the consumer `kinds` values, which describe enclosing syntax rather than usage (finding G2-F6): decide or split a row.

## Evidence

- Surface audit 2026-11-20 (server 5.1.1), finding G2-F5: count 2 with no truncation signal; unlimited call returns 5.
