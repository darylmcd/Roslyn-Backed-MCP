# prompt-error-catch-retirement-core-analysis — Retire prompt catches in core and analysis handlers

**row:** `prompt-error-catch-retirement-core-analysis` · **pri:** `High` · **size:** `M` · **deps:** `prompt-call-error-filter-boundary`

## Anchors

- `src/RoslynMcp.Host.Stdio/Prompts/RoslynPrompts.cs`
- `src/RoslynMcp.Host.Stdio/Prompts/RoslynPrompts.AnalysisWorkflows.cs`
- `tests/RoslynMcp.Tests/PromptSmokeTests.cs` — extend with one table-driven core/analysis sentinel case.

## Acceptance

- [ ] Remove every non-cancellation catch that returns `PromptMessageBuilder.CreateErrorMessage` from the two anchored handler groups.
- [ ] Successful prompt text, role, ordering, and contextual resource reads remain unchanged.
- [ ] A table-driven representative from each group proves exceptions reach the shared boundary and never become successful prompt content.
- [ ] No duplicate logging or sanitization policy remains in the handlers.

## Evidence

- Four core handlers and six analysis workflow handlers currently swallow failures and return successful user-role messages containing raw exception text.
