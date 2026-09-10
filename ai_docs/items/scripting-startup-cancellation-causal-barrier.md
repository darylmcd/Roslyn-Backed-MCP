# scripting-startup-cancellation-causal-barrier — Make startup cancellation coverage causal

**row:** `scripting-startup-cancellation-causal-barrier` · **pri:** `Medium` · **size:** `S` · **deps:** `scripting-supervisor-outer-cancellation-contended-timeout`

## Anchors

- `tests/RoslynMcp.Tests/ScriptingServiceTests.cs`

## Acceptance

- [ ] The startup-cancellation regression observes worker startup through an explicit causal barrier instead of a per-await wall-clock timeout.
- [ ] The test retains a method-level deadlock guard and proves cancellation releases capacity before a succeeding recovery evaluation.
- [ ] The controlled shape remains repeatable enough to expose a startup/cancellation ordering regression.

## Regression

Run five controlled startup-cancel-recovery repetitions with task-completion barriers and no per-await timing budget.
