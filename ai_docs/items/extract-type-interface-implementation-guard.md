# extract-type-interface-implementation-guard — Preserve source interface contracts

**row:** `extract-type-interface-implementation-guard` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/TypeExtractionService.cs:56-58`
- `src/RoslynMcp.Roslyn/Services/TypeExtractionService.cs:119-127`
- `tests/RoslynMcp.Tests/TypeExtractionTests.cs`

## Acceptance

- [ ] `extract_type_preview` refuses moving a member that satisfies an interface on the source type, or produces a behavior-preserving forwarder that leaves the source compiling.
- [ ] A red-first regression exercises an interface implementation, applies the preview when allowed, and checks for no CS0535.

## Evidence

- Parent `extract-type-breaks-interfaces-and-publicizes-fields`: selected member removal at `TypeExtractionService.cs:119-127` does not check interface obligations.

## Context

- Split from the parent row; private field accessibility and whole-file formatting are independent mechanisms tracked separately.
