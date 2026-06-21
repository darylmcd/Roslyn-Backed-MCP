# exception-flow-throwsite-test-and-arm-dedup — throw-site exclusion test + switch-arm dedup

## Anchors

- `tests/RoslynMcp.Tests/ExceptionFlowServiceTests.cs` (throw-site tests cover only the assignable/positive path)
- `src/RoslynMcp.Roslyn/Services/ExceptionFlowService.cs` (the `ThrowStatementSyntax`/`ThrowExpressionSyntax` switch arms repeat an identical bounded-add block; `TryBuildThrowSite` + `IsAssignableTo` throw-branch)

## Acceptance

- [ ] A test throws an exception type NOT assignable to the traced type and asserts it is ABSENT from `result.ThrowSites`; a sibling test throws a subtype and asserts it IS present (exclusion side of `IsAssignableTo` verified).
- [ ] The bounded-add (`if count < AbsoluteMaxResults add else overflow++`) pattern appears once (a local helper), not copy-pasted across the throw-node switch arms.

## Evidence

PR #1015 (trace_exception_flow throw sites) tests only the positive throw-site path, leaving the no-false-positive side unverified; the two throw arms duplicate the bounded-add block. Source: 2026-06-21 backlog-sweep code-quality review of PR #1015 (one medium test-gap + one low dedup).

## Context

Test-coverage gap + small refactor in the same service; bundled as one follow-on.
