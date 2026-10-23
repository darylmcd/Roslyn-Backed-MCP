# native-condition-unused-frame-descriptor-materialization — native-condition-unused-frame-descriptor-materialization

**row:** `native-condition-unused-frame-descriptor-materialization` · **pri:** `Medium` · **size:** `M`

## Problem

`NativeConditionEvidence.Bind` serializes exact XML descriptors for every profiler element, including large singleton target definitions with no semantic consumer or reachable ambiguity refusal. Every fresh graph repeats that unused work.

## Anchors

- `src/RoslynMcp.Roslyn/Services/NativeConditionEvidence.cs`: `Bind`, descriptor cache and `ElementOrigin` guard.
- `tests/RoslynMcp.Tests/NativeConditionElementBindingTests.cs`: exact same-line origins, occurrences and skipped scopes.
- `tests/RoslynMcp.Tests/NativeConditionProtocolTests.cs`: exact native operand and replay controls.

## Acceptance

- Seed every Element frame whose raw file/line/name bucket has more than one candidate; exact candidates are a subset, so every possible ambiguity refusal remains reachable.
- Seed all Condition frames and all Items Element frames.
- Close retained frames over all ancestors and every peer with the exact original occurrence-counter key; include peers' dependencies transitively.
- Keep exact descriptor equality, scope selection, occurrence increments and the original ElementOrigin assertion unchanged for retained frames.
- Excluded frames must have at most one raw candidate, no semantic consumer, and no retained ancestor/counter dependency. Do not omit guards merely because a pass is unused.
- Pass native condition, same-line origin, imported/private-cache, duplicate context, and unchanged undo/fork deadline controls; run required full and hosted gates.

## Evidence

| Evidence | Result / limit |
|---|---|
| Earlier timing | 122 Bind calls consumed 1.832s inclusive during an undo diagnostic; unused descriptor cost not isolated. |
| Rejected approach | Whole-pass omission could drop reachable ElementOrigin refusals; never installed. |
| Reviewed implementation | Capture-local frame dependency closure preserves all potentially reachable guard checks, ancestors and exact occurrence peers. No global graph or authority cache. |
| Current acceptance | Focused34: 226/229 passed; three unchanged deadline failures remain. SDK diagnostic f92ba714: 4550 element frames, 1729 retained, 2821 excluded; excluded descriptor upper bound 1224830 characters. This is measured avoided work, not deadline acceptance. |
