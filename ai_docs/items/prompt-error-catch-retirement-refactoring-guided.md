# prompt-error-catch-retirement-refactoring-guided — Retire prompt catches in refactoring and guided handlers

**row:** `prompt-error-catch-retirement-refactoring-guided` · **pri:** `High` · **size:** `M` · **deps:** `prompt-call-error-filter-boundary,prompt-error-catch-retirement-core-analysis`

## Anchors

- `src/RoslynMcp.Host.Stdio/Prompts/RoslynPrompts.RefactoringWorkflows.cs`
- `src/RoslynMcp.Host.Stdio/Prompts/RoslynPrompts.GuidedWorkflows.cs`
- `src/RoslynMcp.Host.Stdio/Prompts/PromptMessageBuilder.cs`
- `tests/RoslynMcp.Tests/PromptSmokeTests.cs` — extend with one table-driven refactoring/guided sentinel case.

## Acceptance

- [ ] Remove every non-cancellation catch that returns `PromptMessageBuilder.CreateErrorMessage` from the two anchored handler groups.
- [ ] Successful prompt text, role, ordering, and contextual resource reads remain unchanged.
- [ ] A table-driven representative from each group proves exceptions reach the shared boundary and never become successful prompt content.
- [ ] Delete the error-message builder after all production consumers are gone; keep success-message construction cohesive.

## Evidence

- Four refactoring handlers and one guided handler currently convert exceptions into successful prompts with raw `ex.Message`.
- The centralized builder makes the unsafe result consistent, but does not create a protocol error boundary.
