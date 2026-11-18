# generated-interface-reference-assembly-identity — Qualify cross-compilation assembly identity

**row:** `generated-interface-reference-assembly-identity` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Helpers/GeneratedInterfaceReference.cs:28-46`
- `tests/RoslynMcp.Tests/InterfaceSignatureReferenceRouteTests.cs:277-302`

## Acceptance

- [ ] Reproduce same-name, same-containing-type-chain symbols from distinct assemblies using versioned or aliased references. Exercise the cross-compilation comparison and each reachable generated-reference binding and consumer-rewrite route.
- [ ] If a reachable collision is confirmed, distinguish the actual assembly or solution-project identity. Preserve valid cross-compilation matches and existing nested-type controls. Validate each affected public route against the current-major contract before selecting a fix.

## Evidence

- 2026-11-18 (PR 2148 review): full assembly identity (name, version, culture, key) is not a correct fix as is. Two compilations of one wildcard-version, non-deterministic assembly (`1.0.*`, Deterministic=false) can report different versions, giving timing-dependent false mismatches. Compare name plus public key token, or compare version only when both sides are metadata references, since a source project's version is not stable across forks. Reachable from `SiblingInterfaceRepin` via `TryCreate` and its binding check, and from every `IsSame` caller. Cover with an in-memory test: two compilations of assembly `A`, version 1.0 vs 2.0 must differ, and the same version rebuilt must stay the same.
- At `c705e2e1`, `GeneratedInterfaceReference.IsSameAcrossCompilations` compares the namespace and containing-type chain, then uses `ContainingAssembly.Identity.Name` for the outer assembly. The signature-route fixture already constructs two projects with the same simple assembly name. This source observation needs a binding witness for the helper.

## Context

- Treat identical simple names as a candidate collision. Reuse same-assembly-name fixtures where their reference identities model the route.
