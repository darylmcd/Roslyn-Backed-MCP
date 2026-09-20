# donotparallelize-audit-wave-40 — Re-audit serialization opt-outs wave 40

**row:** `donotparallelize-audit-wave-40` · **pri:** `Medium` · **size:** `S` · **deps:** `donotparallelize-audit-wave-01,donotparallelize-audit-wave-02,donotparallelize-audit-wave-03,donotparallelize-audit-wave-04,donotparallelize-audit-wave-05,donotparallelize-audit-wave-06,donotparallelize-audit-wave-07,donotparallelize-audit-wave-08,donotparallelize-audit-wave-09,donotparallelize-audit-wave-10,donotparallelize-audit-wave-11,donotparallelize-audit-wave-12,donotparallelize-audit-wave-13,donotparallelize-audit-wave-14,donotparallelize-audit-wave-15,donotparallelize-audit-wave-16,donotparallelize-audit-wave-17,donotparallelize-audit-wave-18,donotparallelize-audit-wave-19,donotparallelize-audit-wave-20,donotparallelize-audit-wave-21,donotparallelize-audit-wave-22,donotparallelize-audit-wave-23,donotparallelize-audit-wave-24,donotparallelize-audit-wave-25,donotparallelize-audit-wave-26,donotparallelize-audit-wave-27,donotparallelize-audit-wave-28,donotparallelize-audit-wave-29,donotparallelize-audit-wave-30,donotparallelize-audit-wave-31,donotparallelize-audit-wave-32,donotparallelize-audit-wave-33,donotparallelize-audit-wave-34,donotparallelize-audit-wave-35,donotparallelize-audit-wave-36,donotparallelize-audit-wave-37,donotparallelize-audit-wave-38,donotparallelize-audit-wave-39`

## Anchors

- `tests/RoslynMcp.Tests/WorkspaceToolsIntegrationTests.cs`
- `tests/RoslynMcp.Tests/WorkspaceValidationTimeoutTests.cs`
- `tests/RoslynMcp.Tests/AssemblyInfo.cs`
- `CI_POLICY.md`

## Acceptance

- [ ] For every `[DoNotParallelize]` in the anchored test files, either remove it after repeated concurrent evidence or retain it with a source-adjacent comment naming the concrete process-global/shared-workspace dependency.
- [ ] Run each affected class repeatedly before and after the decision; a retained opt-out cites the observed failure mechanism, while a removed opt-out stays green across the bounded repetition.
- [ ] Do not weaken assertions, add sleeps, or classify unrelated concurrency failures as success.
- [ ] Update `AssemblyInfo.cs` to describe the surviving current ownership model.
- [ ] Record repeated before/after serialized-tail TRX evidence in `CI_POLICY.md` after every earlier wave is complete.

## Evidence

Parent row `test-assembly-donotparallelize-audit` found 124 attributes across 120 files after PR #1431 retired TestBase mutable statics. This wave limits review to at most three attributed test files; the final wave owns the aggregate comment and timing evidence.

## Context

One bounded slice of `test-assembly-donotparallelize-audit`. Children are intentionally independent except the final aggregate wave, which depends on waves 01–39.

