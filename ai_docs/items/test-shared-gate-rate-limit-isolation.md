# test-shared-gate-rate-limit-isolation — Isolate assembly test rate limits

**row:** `test-shared-gate-rate-limit-isolation` · **pri:** `High` · **size:** `S` · **deps:** `—`

## Anchors

- `tests/RoslynMcp.Tests/TestInfrastructure/TestServiceContainer.cs`
- `tests/RoslynMcp.Tests/TestInfrastructure/TestAssemblyFixture.cs`
- `tests/RoslynMcp.Tests/TestAssemblyFixtureTests.cs`

## Acceptance

- [ ] The assembly fixture no longer shares the production default 120-request/60-second budget across the whole parallel suite.
- [ ] Production defaults and dedicated rate-limit behavior remain unchanged; no per-test reset hook is introduced.

## Regression

Drive more than 120 concurrent no-op load-gate calls through an isolated `TestAssemblyFixture`; none fail with rate-limit exhaustion, and fixture disposal completes cleanly.
