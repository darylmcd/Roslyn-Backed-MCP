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
