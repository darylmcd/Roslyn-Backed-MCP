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
