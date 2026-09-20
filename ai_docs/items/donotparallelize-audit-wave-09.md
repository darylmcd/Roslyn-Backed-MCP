# donotparallelize-audit-wave-09 — Re-audit serialization opt-outs wave 09

**row:** `donotparallelize-audit-wave-09` · **pri:** `Medium` · **size:** `S`

## Anchors

- `tests/RoslynMcp.Tests/FetchMcpResourceReadinessTests.cs`
- `tests/RoslynMcp.Tests/FindImplementationsCorlibHintTests.cs`
- `tests/RoslynMcp.Tests/FindOverloadsTests.cs`

## Acceptance

- [ ] For every `[DoNotParallelize]` in the anchored test files, either remove it after repeated concurrent evidence or retain it with a source-adjacent comment naming the concrete process-global/shared-workspace dependency.
- [ ] Run each affected class repeatedly before and after the decision; a retained opt-out cites the observed failure mechanism, while a removed opt-out stays green across the bounded repetition.
- [ ] Do not weaken assertions, add sleeps, or classify unrelated concurrency failures as success.

## Evidence

Parent row `test-assembly-donotparallelize-audit` found 124 attributes across 120 files after PR #1431 retired TestBase mutable statics. This wave limits review to at most three attributed test files; the final wave owns the aggregate comment and timing evidence.

## Context

One bounded slice of `test-assembly-donotparallelize-audit`. Children are intentionally independent except the final aggregate wave, which depends on waves 01–39.

