# type-extraction-bare-invalid-operation-refusals — Unify TypeExtractionService refusals on a typed shape

**row:** `type-extraction-bare-invalid-operation-refusals` · **pri:** `Low` · **size:** `S` · **deps:** `type-extraction-service-refusals-public-message`

## Anchors

- `src/RoslynMcp.Roslyn/Services/TypeExtractionService.cs` (multi-part partial check, primary-ctor branch, bodyless / named `this(...)` / unresolved-target constructor topology branches)
- `tests/RoslynMcp.Tests/TypeExtractionTests.cs`

## Acceptance

- [ ] Each listed refusal throws a typed exception (or the existing structured blocking-dependency shape) instead of a bare `InvalidOperationException`, with tests asserting the type.

## Evidence

- PR #2014 implementer sighting; the blocking-dependency family already carries structured refusals.

## Notes

2026-11-11 remediation: excluded, major. Converting the bare InvalidOperationException refusals to a typed shape or PublicInvalidOperationException publishes/changes public messages; same bucket as type-extraction-service-refusals-public-message (Defer, next major per operator 2026-11-09). InvalidOperationConstructionRatchetTests fingerprints the plain constructions, so a reword is a new identity and baseline edits are forbidden.
