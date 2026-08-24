# cancellation-invariant-regression-locks — the audit's "no gap" conclusions rest on untested invariants

## Anchors

- `src/RoslynMcp.Roslyn/Services/ScriptExecutionSupervisor.cs`
- `src/RoslynMcp.Roslyn/Services/ScriptingService.cs`
- `tests/RoslynMcp.Tests/WorkspaceForkApplyServiceTests.cs`

## Acceptance

- [ ] A regression pins `ScriptExecutionSupervisor`'s invariant that the ONLY `OperationCanceledException` it can throw is tied to the caller's ambient token — i.e. an internal-budget expiry yields `ScriptExecutionOutcome.TimedOut()` as a result value, never an exception.
- [ ] A regression covers `WorkspaceForkApplyService`'s timeout path, which is currently uncovered.
- [ ] Both tests fail if the safe pattern is reverted (verify by temporarily reverting, not by inspection).

## Evidence

The `gate-owned-timeout-cts-oce-classification-audit` initiative closed with zero code changes by concluding "no gap" at five anchors. Two of those conclusions depend on invariants that nothing currently enforces:

- `ScriptExecutionSupervisor` is safe **because** its internal-timeout token is only observed by a synchronous worker that converts cancellation into a result value. If a future edit lets that token throw, the audit's conclusion silently becomes false with no test to catch it.
- `WorkspaceForkApplyService`'s timeout reclassification path has no test coverage at all.

An audit that ships no code leaves no regression behind; this row is what keeps its conclusions true.

## Context

Prescribed by the initiative's own plan stanza (Risk 2 + handoff notes: "file Risk (2)'s regression-test row via `backlog.mjs add`") and missed at closeout. Surfaced by the sweep's cold self-review.
2026-08-24 partial hardening only: `ScriptExecutionSupervisor` now catches unexpected worker callback exceptions and converts cancellation tied to its timeout token into a `TimedOut` outcome; a direct runtime-exception boundary regression was added. Keep this row open: it still requires the explicit internal-budget invariant and `WorkspaceForkApplyService` timeout regression in its acceptance.
