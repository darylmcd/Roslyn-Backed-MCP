# split-service-with-di-identity-semantics — Preserve original service graph identity

**row:** `split-service-with-di-identity-semantics` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/SymbolRefactorService.cs`

## Acceptance

- [ ] Preserve actual source receiver, state and constructor identity, including `this`, runtime type, equality, delegate targets and event unsubscription; warnings do not substitute for representable semantics.
- [ ] Preserve record equality/hash/with/clone without injecting partition references into record equality state; each actual source/clone gets its own correctly published graph.
- [ ] Preserve original dependency initialization and service/interface/factory/receiver/lifetime registrations; one actual owner's partitions share original state without collapsing distinct transient roots.
- [ ] Prove reference identity and initialization counts across direct construction and three DI lifetimes, plus concurrent graph publication and collectability. Preserve struct/ref-struct byref ownership or prove a specific unrepresentable request before atomic refusal; no blanket source-kind refusal.
- [ ] Close only after complete acceptance evidence; selected `split-service-refuse-cross-partition-references` owns these forced companions and is deferred at110000 for global admission. Public generated-contract changes require that selected ADR/migration.

## Evidence

- Historical cold review2026-09-25, `split-service-with-di-facade-drops-sibling-types`, findings9/10/13 identified pre-existing identity defects.
- Source-only re-vet2026-10-10 at a4a826dc, runtime unchanged on1cd4a90: `SymbolRefactorService.cs:865-985,1217-1263` copies fields and replaces facade construction; reference roots and record fields change identity. No new identity runtime reproduction or implementation proof claimed.

## Context

| Supersession | Disposition |
|---|---|
| Historical blanket `this` warnings and record refusal | Replaced with root-cause identity preservation; warnings alone cannot repair representable behavior. |
| Shared owner graph | Side-effect-free publication with one wrapper per partition; preserve original fields/constructors and weak-key collection. |
| Atomic selected scope | Seven production/four tests/ADR-index-fragment at110000; global Rule5 cap80000 currently rejects it. Other agent owns tooling remediation. |
| Tracking identity | Original row/id/priority retained; no duplicate row or source change. Full selected coupling scope remains preserved. |
