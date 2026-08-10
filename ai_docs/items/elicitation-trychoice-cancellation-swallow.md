# elicitation-trychoice-cancellation-swallow — stop reporting request cancellation as "user declined"

**row:** `elicitation-trychoice-cancellation-swallow` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Host.Stdio/Elicitation/ElicitationChoicePrompt.cs:118-128` (the `try` awaiting `server.ElicitAsync`)
- `src/RoslynMcp.Host.Stdio/Elicitation/ElicitationChoicePrompt.cs:122` (bare `catch { return null; }`)
- `src/RoslynMcp.Host.Stdio/Tools/SymbolTools.cs:93,105,933,941` (callers that read `null` as "declined")
- `tests/RoslynMcp.Tests/SymbolDisambiguationElicitationTests.cs`

## Acceptance

- [ ] `OperationCanceledException` from the request-scoped token propagates out of `TryElicitChoiceAsync` instead of being converted to `null`. Only `InvalidOperationException` / `McpException` are swallowed, and the swallow logs at debug or warning rather than silently.
- [ ] A test asserts that a pre-cancelled `CancellationToken` surfaces cancellation — not a silent `null` / additive-list fallback — through at least one `SymbolTools` disambiguation path.

## Evidence

- Traced during the code-quality review of PR #1205 (`hoststdio-middleware-tools-namespace-cycle`): the `try` awaits `server.ElicitAsync(request, cancellationToken)` with the caller's own token, and the `catch` has no exception type and no `when` filter, so cancellation is caught and returned as `null`. Callers at `SymbolTools.cs:93-120` / `:933-950` treat `null` as "user declined" and answer with the additive list response.

## Context

The body is a **verbatim relocation** by PR #1205 (which moved it out of `StructuredCallElicitationCoordinator` into the new `Elicitation` namespace to break the Middleware↔Tools cycle); the defect is pre-existing, not introduced by that PR, which is why the reviewer marked it advisory rather than blocking the land.

The inline comment claiming the catch "never masks a real failure" enumerates only `InvalidOperationException` and `McpException` — it does not account for `OperationCanceledException`, which the bare `catch` also absorbs. Net effect: a cancelled request is indistinguishable from a deliberate user decline, and the tool returns a successful-looking list response for work the caller abandoned.

## Notes

- `catch (Exception ex) when (ex is not OperationCanceledException)` is the minimal shape; prefer naming the two expected types explicitly so a genuinely unexpected exception is not absorbed either.
- Members here are `internal`, so no published-surface (Directive #4) concern applies.
