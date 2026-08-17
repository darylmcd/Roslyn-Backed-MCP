# prompt-smoke-tests-concern-split — Split prompt smoke-test concerns

**row:** `prompt-smoke-tests-concern-split` · **pri:** `Low` · **size:** `S` · **deps:** `prompt-workflows-missing-test-coverage,prompt-shim-parameter-binding-complexity-extraction,prompt-error-catch-retirement-core-analysis,prompt-error-catch-retirement-refactoring-guided`

## Anchors

- `tests/RoslynMcp.Tests/PromptSmokeTests.cs`

## Acceptance

- [ ] Split builder smoke tests, prompt-shim binding/error tests, and shared sample/workspace helpers into focused test types.
- [ ] Perform the split after both prompt catch-retirement rows establish the final protocol-error behavior; do not preserve obsolete successful-error assertions.
- [ ] Shared setup has one owner and does not introduce test-order coupling or duplicate workspace loads.
- [ ] Existing prompt test names and coverage remain discoverable and pass in isolation.

## Evidence

- Cold host-hotspot review rejected adding another unrelated binding test to the already multi-concern smoke class.
- Both prompt catch-retirement rows extend this file, so the concern split must target their final test surface rather than race those edits.
