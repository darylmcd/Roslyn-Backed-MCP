# prompt-workflows-missing-test-coverage — Add test coverage for untested prompt workflows

**row:** `prompt-workflows-missing-test-coverage` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Host.Stdio/Prompts/RoslynPrompts.AnalysisWorkflows.cs:270`
- `src/RoslynMcp.Host.Stdio/Prompts/RoslynPrompts.AnalysisWorkflows.cs:318`
- `src/RoslynMcp.Host.Stdio/Prompts/RoslynPrompts.AnalysisWorkflows.cs:371`
- `src/RoslynMcp.Host.Stdio/Prompts/RoslynPrompts.GuidedWorkflows.cs:15`
- `src/RoslynMcp.Host.Stdio/Prompts/RoslynPrompts.RefactoringWorkflows.cs:75`
- `src/RoslynMcp.Host.Stdio/Prompts/RoslynPrompts.RefactoringWorkflows.cs:149`
- `src/RoslynMcp.Host.Stdio/Prompts/RoslynPrompts.RefactoringWorkflows.cs:199`

## Acceptance

- [ ] Each of the seven listed prompt workflows has at least one direct test invocation in tests/RoslynMcp.Tests
- [ ] New tests pass under existing test runner

## Evidence

- Refactor Harness 2.0 pass 1 (`/refactorv2`), Roslyn-backed scoring + adversarial verify where applicable. Full per-cell detail: `ai_docs/reports/20260708T234500Z_roslyn-backed-mcp_refactor-matrix-pass1.md`.
- Contributing cells: S04e-host-server-infrastructure::DG6-testability-obs
