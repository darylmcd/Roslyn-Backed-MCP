# reference-service-dead-iscorlib-single-arg-overload — remove unreferenced IsCorlibAssembly overload

**row:** `reference-service-dead-iscorlib-single-arg-overload` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/ReferenceService.cs:444` (`private static bool IsCorlibAssembly(IAssemblySymbol? assembly) => IsCorlibAssembly(assembly, SpecialType.None);`)
- sole live call site: `src/RoslynMcp.Roslyn/Services/ReferenceService.cs:422` (uses the two-arg overload `IsCorlibAssembly(type.ContainingAssembly, type.SpecialType)`)

## Acceptance

- [ ] The single-arg `IsCorlibAssembly(IAssemblySymbol?)` overload is removed (it has no callers — the only call site uses the two-arg form), or a caller is restored if one was intended.
- [ ] Build + the existing `ReferenceService` tests stay green after removal.

## Evidence

- Code-quality + implementer findings (2026-06-20, compcache batch-a, PR #1005): the single-arg overload at `ReferenceService.cs:444` is dead — `find_references`/grep show no callers; the two-arg overload at `:422` is the only one used. Pre-existing (outside the batch-a diff); flagged per Directive #3.

## Context

Dead code, 1 production file, no behavior change. Trivial removal — bundle with the next `ReferenceService` touch or close as a standalone S row.
