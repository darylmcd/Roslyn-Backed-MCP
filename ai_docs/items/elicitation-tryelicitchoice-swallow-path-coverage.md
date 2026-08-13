# elicitation-tryelicitchoice-swallow-path-coverage — cover the retained InvalidOperationException / McpException swallow paths in TryElicitChoiceAsync

**row:** `elicitation-tryelicitchoice-swallow-path-coverage` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Host.Stdio/Elicitation/ElicitationChoicePrompt.cs:125-140`
- `tests/RoslynMcp.Tests/SymbolDisambiguationElicitationTests.cs`

## Acceptance

- [ ] A test drives each named exception (`InvalidOperationException`, `McpException`) out of `server.ElicitAsync` and asserts `TryElicitChoiceAsync` returns `null` (additive-list fallback preserved) rather than throwing.
- [ ] The two-type enumeration is verified against the MCP SDK's actual transport-gone / client-error shapes; if the SDK also throws e.g. `IOException`/`ObjectDisposedException` on a dead pipe, either add the type or record why the hard-error outcome is intended.

## Evidence

Traced during code-quality review of `elicitation-trychoice-cancellation-swallow`: grepped all four elicitation test suites — no test constructs an `InvalidOperationException` or `McpException` from `ElicitAsync`, so the narrowed catch has no coverage. The two-type enumeration's provenance is the comment the row's diff replaced, which the row's own detail file proves was incomplete (it did not account for `OperationCanceledException`) — so it is not an authoritative list without independent verification.

## Context

Spin-off from the `elicitation-trychoice-cancellation-swallow` row's code-quality review (top-n-remediation run 20260810T233007Z).
## Amendment — 2026-08-13 (backlog-sweep 20260813T172325Z, PR #1242 code-quality review)

Give `ElicitationChoicePrompt` its own test suite as part of this row's work.

After PRs #1237/#1242 collapsed the elicitation forwarders, every direct `ElicitationChoicePrompt.TryElicitChoiceAsync` test call lives in a suite named for a DIFFERENT type — `StructuredCallElicitationCoordinatorTests` and `SymbolDisambiguationElicitationTests` — and no `ElicitationChoicePromptTests.cs` exists (verified by grep + directory listing in the PR worktree). The #1242 diff annotates the mismatch ("pinned here for historical continuity") rather than resolving it.

Routed here rather than as a sibling row because this row already plans `TryElicitChoiceAsync` tests against the same anchors.

**Add to this row's acceptance:** a `tests/RoslynMcp.Tests/ElicitationChoicePromptTests.cs` suite owns the picker's direct-call tests including the null-server short-circuit; no suite named for another type retains a `TryElicitChoiceAsync_*` test that calls `ElicitationChoicePrompt` directly.
