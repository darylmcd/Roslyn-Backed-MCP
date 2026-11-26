# symbol-handle-local-function-owner-roundtrip — Preserve local-function ownership in constructed handles

**row:** `symbol-handle-local-function-owner-roundtrip` · **pri:** `Medium` · **size:** `M` · **verified:** `7b4882c08d94db9906e78d3de4ed4449af8509bb`

## Anchors

- `src/RoslynMcp.Roslyn/Helpers/SymbolHandleSerializer.cs` — `EncodeMethodOwner`
- `tests/RoslynMcp.Tests/ConstructedNamedTypeSymbolHandleTests.cs` — `LocalFunctionTypeOwners_NewHandlesAreDisplayOnly`
- `docs/decisions/0046-method-owned-constructed-type-symbol-handle.md` — Remainder

## Acceptance

- [ ] Identify the exact local-function owner and ordinal inside one compilation. Resolve from a verified workspace declaration and binding, or establish an equivalent exact lexical identity.
- [ ] Distinguish nested and same-signature sibling local functions. Removing the original declaration cannot cause its handle to select a sibling. Cover outer generic scope and declare which edits invalidate the locator.
- [ ] Obtain a separate persisted-format decision and qualify both supported protocol eras before landing. Preserve older handle bytes and the published error categories.

## Evidence

- L2, pre-existing, P2 (I5). Generic local functions are valid source owners outside the type-member lookup. Before the correction, an independently bound local `T` in source-backed `Base<T>` collided with the open `Base<T>` display and fell back to the class-owned symbol. The fail-closed test demonstrates the bounded parent fix.
- A containing method and signature alone cannot preserve lexical identity. If the original `Local<T>` in one block disappears, a same-signature sibling can become the only match. Verify a workspace source anchor and semantic binding before relying on this route.
- Operator chose to defer local-function replay and ship fail-closed behavior on 2026-11-26 (collab seq 339).
