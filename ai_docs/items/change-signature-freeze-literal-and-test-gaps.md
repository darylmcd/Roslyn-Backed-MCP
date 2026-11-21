# change-signature-freeze-literal-and-test-gaps — Tidy caller-info freeze literals and close its test gaps

**row:** `change-signature-freeze-literal-and-test-gaps` · **pri:** `Low` · **size:** `S` · **verified:** `82be4c9b0e7f752e0d899554b037d175a2f6dfcd` · **deps:** `change-signature-unrelated-invocation-rewrite`

## Anchors

- `src/RoslynMcp.Roslyn/Services/ChangeSignatureAddRemovePreviewBuilder.cs`
- `tests/RoslynMcp.Tests/ChangeSignatureBindingsTests.cs`

## Acceptance

- [ ] The compile-driven caller-info freeze (`FreezeDriftedCallerInfoAsync`) writes literals without cosmetic `(int)(10)` casts for edited-call freezes and without a lossy `ToString` for non-string constants (regression shape: assert the exact frozen text for int, string and enum-typed caller-info constants).
- [ ] An unrelated caller-info call that is an object creation or a struct construction keeps its text when the edited call changes (red-first, one test per shape).
- [ ] An unrelated caller-info call in a separate, unchanged document is left untouched (guard test for the skip path).

## Evidence

- Cold implementation review of the change-signature unrelated-invocation PR (rem/cs-sig) follow-ups 2 and 3; implementer report note on the `(int)(10)` cast.

## Context

Same production file as the freeze introduced by `change-signature-unrelated-invocation-rewrite`; one commit and verdict per checkbox.
