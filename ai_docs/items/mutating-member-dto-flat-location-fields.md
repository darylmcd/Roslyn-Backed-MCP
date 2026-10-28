# mutating-member-dto-flat-location-fields — Decide and migrate MutatingMemberDto flat location fields

**row:** `mutating-member-dto-flat-location-fields` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Core/Models/` (`MutatingMemberDto`)
- `docs/decisions/0011-v5-major-release-contract.md`

## Acceptance

- [ ] Decide: give `MutatingMemberDto` a nested `location` and remove flat `filePath`/`line` at the next major, or document it as permanently flat in `docs/product-contract.md`.
- [ ] If migrating, follow `docs/release-policy.md` (deprecate for one released minor before removal).

## Evidence

- ADR 0011 section 2 removes the flat location members from `SymbolDto`, `DiagnosticDto`, `TypeUsageDto`, `PropertyWriteDto` and `MutationCallerDto`; `MutatingMemberDto` still carries flat `filePath`/`line` and no nested `location` (5.0.0 Phase B item 6 report, 2026-10-28).
