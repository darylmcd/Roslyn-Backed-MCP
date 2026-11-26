# metadata-member-handle-aliased-identity — Distinguish aliased metadata member handles

**row:** `metadata-member-handle-aliased-identity` · **pri:** `Medium` · **size:** `M` · **verified:** `e1b828370136a3bc06bedc8133ecf97680d559ca`

## Anchors

- `src/RoslynMcp.Roslyn/Helpers/SymbolHandleSerializer.cs` — `CreateHandle`, `ResolveMetadataMemberAsync`
- `tests/RoslynMcp.Tests/MetadataMemberSymbolHandleTests.cs`
- `docs/decisions/0046-method-owned-constructed-type-symbol-handle.md` — logical-slot precedent

## Acceptance

- [ ] Distinguish imported metadata overloads whose parameter types have the same metadata name in different aliased assemblies. Resolve the intended declaration in one compilation and refuse ambiguous or stale bindings.
- [ ] Qualify a later snapshot exposing only a lookalike member with the same documentation ID. Its different signature binding must not silently select a replacement member.
- [ ] Obtain a persisted-format decision and qualify legacy bytes and error behavior across both supported protocol eras before changing emission.

## Evidence

- L2, pre-existing, P2 (I5). An imported containing type may declare distinct overloads over independently referenced assemblies whose parameter type names coincide. `CreateHandle` stores a documentation declaration ID, which does not include parameter assembly identity. `ResolveMetadataMemberAsync` enumerates that containing type's members by declaration ID. Two matches return null, and a later snapshot with just a lookalike match can select a different member.
- The method-owned construction change preserves the existing non-constructed member recipe. The structural method signature in ADR 0046 provides a precedent for this separate scope. The exact representation still requires its own D8 and Rule 1 qualification.
- Collaboration findings and routing are recorded at seqs 352–355 on 2026-11-26.
