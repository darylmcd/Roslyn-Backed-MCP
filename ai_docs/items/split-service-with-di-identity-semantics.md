# split-service-with-di-identity-semantics — Refuse splits that change object identity semantics

**row:** `split-service-with-di-identity-semantics` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/SymbolRefactorService.cs`

## Acceptance

- [ ] A moved method that uses `this` (passes it, calls `GetType()`, relies on overridden `Equals`/`ToString`) is refused or flagged in the preview warnings.
- [ ] A `record` source type is refused (compiler-generated equality would compare partition references instead of moved state).
- [ ] One constructor parameter copied into two fields, or a readonly field injected into both facade and partition, is flagged: with transient DI lifetimes one shared instance becomes several.

## Evidence

- Cold review of `split-service-with-di-facade-drops-sibling-types` (2026-09-25, findings 9, 10, 13). Mostly pre-existing on `main`.

## Context

Filed 2026-09-25 while shipping the sibling-types fix.

Source-only re-vet 2026-10-10 at a4a826dc, unchanged runtime on1cd4a90: SymbolRefactorService865-985/1217-1263 copies source fields/constructors into partitions and replaces facade constructor. Complete selected split-service-refuse-cross-partition-references amendment1 must repair this same mechanism at110000: retain original source state/constructor and delegate targets, preserve record equality/hash/with/clone using owner graphs outside record instance state, preserve per-actual-owner graph identity across facade/cross-calls and three DI lifetimes without collapsing independent transient roots. Reference identity graphs must publish one side-effect-free wrapper per partition and remain collectable; preserve struct/ref-struct byref ownership or prove a specific unrepresentable request, never blanket-refuse valid source kinds. Selected atomic scope is deferred for externally owned Rule5 admission; no implementation/runtime proof or row closure. Earlier blanket refusal acceptance is not authorization for a lesser fix.
