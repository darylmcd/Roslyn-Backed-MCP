# repin-alias-to-error-type-not-rebound — Probe re-pin of an alias to an unresolved type

**row:** `repin-alias-to-error-type-not-rebound` · **pri:** `Low` · **size:** `S` · **verified:** `12bc2c8b5395056e11d2772b593347500033e5b6`

## Anchors

- `src/RoslynMcp.Roslyn/Helpers/SiblingInterfaceRepin.cs`:286-293 — `earlier` null falls to `_ => true`.

## Acceptance

- [ ] A test with `using IItemService = Missing.T;` (unresolved) in a document that the generated interface would capture shows whether the meaning changes after generation.
- [ ] If it can change (an error becomes a binding to the new interface), the re-pin refuses with file and line; otherwise the test pins the no-op.

## Evidence

- iface-extraction-b report (PR 2148), round 5 row sketch: "an alias to an unresolved (error) type: earlier symbol is null so the reference is not seen as rebound; the K1 bind check does not cover it".
- Verified at 12bc2c8b: the `unchanged` switch maps a null `earlier` to `true` (line 293).
