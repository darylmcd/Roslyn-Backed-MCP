# native-condition-import-cache-repeated-inventory — native-condition-import-cache-repeated-inventory

**row:** `native-condition-import-cache-repeated-inventory` · **pri:** `Medium` · **size:** `S`

## Problem

| Field | Evidence |
|---|---|
| Priority / size | Medium / S |
| Production anchor | `src/RoslynMcp.Roslyn/Services/BuildPathRoles.cs`: `Capture` / `CaptureCondition` |
| Mechanism | Every executed condition reconstructs a hash set of all evaluated import paths plus the root, including non-import predicates that never use the set. Work grows with executed predicates times imported roots. |
| Safe boundary | The completed native project's imports remain unchanged throughout this synchronous capture. Reuse one lazy local set only for the existing executed Import/ImportGroup fallback. |
| Recorded timing | `undo-admission-overlap-9240a8a99f8f4f06b81e9873ce0b17c3/diagnostic.log`: False path-role capture 61 calls / 1685.317ms. This includes all path-role work; no isolated saving claim. |

## Acceptance

- [ ] Build the exact evaluated import-path/root lookup at most once per capture, only when the existing cached-import fallback needs it.
- [ ] Preserve actual query precedence, native short-circuit execution, exact directory suffix semantics, and import-only PRE-cache admission.
- [ ] Pass existing `NativeConditionProtocolTests.ImportPredicate_UsesCacheStateAtActualNativeExecution`, `CachedProjectPredicate_DistinguishesNativeExecutionFromShortCircuit`, `CachedImportFunction_UsesObservedNativeArgumentForPrivateSelection`, and `CachedProjectFile_WithDirectorySuffix_RetainsNativeFalseResult` controls.
- [ ] Preserve all authority checks, original deadlines, fresh evaluation boundaries, and full shipping gates. Record measured combined acceptance; no flake/retry classification.

## Current evidence

| Evidence | State |
|---|---|
| Scratch patch | `cached-import-path-index-proposal/proposal.patch`; apply-check passes against current source. |
| Runtime verification | Pending parent-owned serialized run. No product edits or tests performed by this reviewer. |

Keep OPEN pending gates. This prerequisite does not advance the paused batch.
