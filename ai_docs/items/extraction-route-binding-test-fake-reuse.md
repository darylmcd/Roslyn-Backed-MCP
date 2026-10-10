# extraction-route-binding-test-fake-reuse — Share duplicated route test fakes

**row:** `extraction-route-binding-test-fake-reuse` · **pri:** `Low` · **size:** `S`

## Anchors

- `tests/RoslynMcp.Tests/ExtractionApplyRouteBindingTests.cs:24-28`
- `tests/RoslynMcp.Tests/ToolDispatchTests.cs`

## Acceptance

- [ ] Probe every duplicated route-binding fake mechanism; extract the genuinely shared gate/store/service behavior into an internal test fixture and update all matching consumers.
- [ ] Preserve explicit behavioral variants, call counts, required interface members, failure ordering and test isolation. Do not merge semantically different fakes or add default implementations that conceal missing contract updates.
- [ ] Remove stale comments justifying duplication and retain all wrong-family, unbound-token and dispatch redaction regressions. Run all affected route-binding classes.

## Evidence

- Source-only observation at `7322a2d1810743427185add91f4e44442ebfb1a9`: ExtractionApplyRouteBindingTests:24-28 explicitly documents local copies of ToolDispatchTests private nested fakes as a coupling workaround; defining fakes start at :178 and :211.
- Both files exercise the same shared apply-dispatch contracts. Shared interface changes currently require editing duplicate test implementations independently. Re-vet exact copies and non-identical variants before extraction; no runtime defect or repair is claimed here.

## Context

- Separate test-fixture duplication mechanism observed during extraction formatting blast-radius review; no change to the active executor Scope.
