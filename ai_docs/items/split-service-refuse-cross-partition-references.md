# split-service-refuse-cross-partition-references — Preserve semantic references across service partitions

**row:** `split-service-refuse-cross-partition-references` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/SymbolRefactorService.cs:445`
- `src/RoslynMcp.Core/Services/ISymbolRefactorService.cs:26`
- `tests/RoslynMcp.Tests/CompositeSplitServiceDiPreviewTests.cs`

## Acceptance

- [ ] Bind moved and retained member references semantically, including actual receivers and overloads; preserve source-owned state and identity across kept, base, static and cross-partition references. Do not blanket-refuse shapes that source-owned composition can represent correctly.
- [ ] Prove preview/apply/reload compilation and runtime equivalence for overloads, method groups, recursion/cycles, local shadowing, explicit other-instance receivers and initializer/constructor identity under transient/scoped/singleton registrations.
- [ ] Preserve independent valid previews; refuse only genuinely unresolved/ambiguous binding or proven unsupported external shapes before token storage, using authored input-free public guidance.
## Evidence

- Cold review of `split-service-with-di-facade-drops-sibling-types` (2026-09-25, finding 6; 2026-09-25 ctor-injection-precision review). Pre-existing on `main`: `BuildPartitionFile` (`SymbolRefactorService.cs:768`) emits only moved methods plus instance fields; `BuildForwardingMethod` (`:1254`) builds forwarding stubs. The defects are visible (non-compiling previews), not silent, hence Medium.

## Context

- Split child of `split-service-with-di-refuse-unsupported-method-shapes` (plan 20261001T034545Z; one regression shape each). Siblings share `SymbolRefactorService.cs` and `tests/RoslynMcp.Tests/CompositeSplitServiceDiPreviewTests.cs`, so they are chained by `deps`. Each ships independently: a refusal or a correct forward, never a half-supported shape.

Planning re-vet 2026-10-07: SymbolRefactorService.cs:348-362 selects declarations by spelling; :445-510 copies fields using identifier-name matches; :872-909 repeats that analysis for retained facade members. Blanket refusal suppresses representable operations without repairing binding/state ownership. The complete plan must replace name-only discovery and preserve original semantic receivers and shared state; no implementation is claimed.
