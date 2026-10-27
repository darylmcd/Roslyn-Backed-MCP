# extract-type-partial-overload-identity — Handle partial method pairs and partial overload ambiguity

**row:** `extract-type-partial-overload-identity` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/TypeExtractionService.cs:290-291` — ambiguity check skipped when every candidate is partial.
- `src/RoslynMcp.Roslyn/Services/TypeExtractionService.cs:583-587` — helper emitted as non-partial `public sealed class`.

## Acceptance

- [ ] Red first: `public partial class P1 { partial void Hook(int x); partial void Hook(int x) { } }`, extract `Hook`: CS0751 in the non-partial helper.
- [ ] Two distinct partial overload pairs with the same name are refused as ambiguous.
- [ ] A single pair either moves as a matching partial pair into a partial helper or is refused (typed).

## Evidence

- 2026-10-27: CS0751 observed on main's rendered helper.

## Context

- From the deleted extraction archive's proposed row of the same name.
