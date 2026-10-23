# declaration-ordinal-quadratic-scan — declaration-ordinal-quadratic-scan

**row:** `declaration-ordinal-quadratic-scan` · **pri:** `Medium` · **size:** `S`

## Problem

| Field | Evidence |
|---|---|
| Mechanism | `BuildPathRoles.Capture` local `Add` enumerated `definition.AllChildren` twice per declaration: prefix scan for its reference identity, then complete count. Many declarations in one imported XML root repeatedly walked the same immutable tree. |
| Scope | Native declaration coordinate capture; no change to fresh evaluation, filesystem reads, grants or boundary checks. |
| Production anchor | `src/RoslynMcp.Roslyn/Services/BuildPathRoles.cs`: `Capture`, local `Add`. |
| Regression anchor | `tests/RoslynMcp.Tests/NativeDeclarationOrdinalTests.cs`: exact authored ordinal, nested descendants, same-line duplicate elements and implicit SDK import controls. |

## Acceptance

- [ ] Build a lazy, capture-local ordinal index with reference-keyed native XML roots and elements; enumerate each root's `AllChildren` at most once for declaration ordinal lookup.
- [ ] Preserve original one-based nested `AllChildren` order and distinct occurrences of structurally identical same-line elements.
- [ ] Skip implicit SDK imports absent from the authored tree; never invent an ordinal or use file/name/line equality as element identity.
- [ ] Preserve emitted declaration order, multiplicity and existing deduplication semantics; retain all native evaluation and authority checks.
- [ ] Verify native controls and required restore integration gates before closing.

## Evidence

| Check | Current-session result |
|---|---|
| Source | Capture-local reference index installed by parent; source review preserves exact lookup semantics. |
| Focused native regression | Parent reports `declaration-ordinal24.trx`: 1 passed. |
| Broader integration | Parent reports combined24 running with an original deadline failure; no aggregate pass or completion claim. |
| Performance | Repeated root traversal removed structurally; no measured timing improvement claimed. |

Keep this row open pending required integration and shipping gates. Do not advance the paused batch.
