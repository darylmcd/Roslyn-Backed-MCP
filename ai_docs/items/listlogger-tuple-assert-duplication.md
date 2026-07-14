# listlogger-tuple-assert-duplication — dedupe the ListLogger<T> test helper

**row:** `listlogger-tuple-assert-duplication` · **pri:** `Low` · **size:** `S`

## Anchors

- `tests/RoslynMcp.Tests/HardeningBehaviorTests.cs` (private `ListLogger<T>`)
- `tests/RoslynMcp.Tests/ValidateRecentGitChangesTests.cs` (private `ListLogger<T>`)
- `tests/RoslynMcp.Tests/TestInfrastructure/` (target home)

## Acceptance

- [ ] One shared `ListLogger<T>` under `TestInfrastructure/`; both test classes consume it; no private copies remain.
- [ ] The kill-failure "found-entry" assertion pattern (`Assert.AreNotEqual(default, entry, …)` over the tuple) stays consistent at both sites.

## Evidence

- Copy-paste flagged during the 2026-07-14 MSTest assert migration (both copies needed the identical MSTEST0032 fix).
