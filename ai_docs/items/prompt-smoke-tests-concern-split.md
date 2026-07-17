# prompt-smoke-tests-concern-split — Split prompt smoke-test concerns

**row:** `prompt-smoke-tests-concern-split` · **pri:** `Low` · **size:** `S`

## Anchors

- `tests/RoslynMcp.Tests/PromptSmokeTests.cs`

## Acceptance

- [ ] Split builder smoke tests, prompt-shim binding/error tests, and shared sample/workspace helpers into focused test types.
- [ ] Shared setup has one owner and does not introduce test-order coupling or duplicate workspace loads.
- [ ] Existing prompt test names and coverage remain discoverable and pass in isolation.

## Evidence

- Cold host-hotspot review rejected adding another unrelated binding test to the already multi-concern smoke class.
