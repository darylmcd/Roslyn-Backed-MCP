# symbol-handle-method-type-parameter-roundtrip — Preserve method-owned type parameters in constructed handles

**row:** `symbol-handle-method-type-parameter-roundtrip` · **pri:** `Medium` · **size:** `M` · **verified:** `19d3c5330047684ce5de3b7c6d819ff88f5f4144`

## Anchors

- `src/RoslynMcp.Roslyn/Helpers/SymbolHandleSerializer.cs` — `EncodeType`, `ResolveType`
- `tests/RoslynMcp.Tests/ConstructedNamedTypeSymbolHandleTests.cs` — `MethodOwnerAndOverBudgetTypes_KeepLegacyBehavior`
- `docs/decisions/0045-constructed-named-type-symbol-handle.md` — Remainder

## Acceptance

- [ ] Replay a constructed named type over a method-owned type parameter with the exact owning method and ordinal in one compilation. Preserve supported legacy handles and the public error categories.
- [ ] Qualify ambiguity, aliased overloads and local-function owners with red-first evidence. Return any required payload change for a contract decision before implementation.

## Evidence

- L1, pre-existing, P2 (I5). A valid source method `M<T>(List<T> value)` produces a constructed-type handle whose replay returns null. The bounded identity correction deliberately preserves that legacy path.
- Retained unmerged witness `witness/symbol-handle-method-type-parameter` at `f1281a582befea93a4125c22daaf2585e7ba8d59`. Run `ConstructedNamedTypeSymbolHandleTests.MethodOwner_RoundTripsExactConstruction` through the machine validation gate. On the bounded candidate, the one test fails because resolution returns null. No failing test is part of the shipped suite.
- Operator approved the bounded type-owner design and explicitly deferred method owners on 2026-11-26 (collab decisions 154 and 155).
