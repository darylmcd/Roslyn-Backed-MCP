# metadata-overload-public-reference-set-test — Assert distinct public reference results

**row:** `metadata-overload-public-reference-set-test` · **pri:** `Low` · **size:** `S`

## Anchors

- `tests/RoslynMcp.Tests/SymbolDisambiguationElicitationTests.cs:526`

## Acceptance

- [ ] A controlled workspace calls distinct external overloads and public `find_references` with each advertised candidate handle returns the corresponding distinct source reference set.
- [ ] Preserve exact serializer identity tests and stable metadata definition NotFound behavior.

## Evidence

- Cold metadata-handle review on 2026-10-31: the public roundtrip test asserts success for both `Process.Kill` candidate handles but does not distinguish their reference sets; the direct serializer test covers exact symbol identity separately.
