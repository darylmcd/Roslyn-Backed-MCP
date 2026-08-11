# isolated-workspace-slnx-surgery-consolidation — consolidate copied-.slnx project-list surgery into IsolatedWorkspaceTestBase

**row:** `isolated-workspace-slnx-surgery-consolidation` · **pri:** `Low` · **size:** `S`

## Anchors

- `tests/RoslynMcp.Tests/IsolatedWorkspaceTestBase.cs:36`
- `tests/RoslynMcp.Tests/ScaffoldingFirstTestFileTests.cs:368`
- `tests/RoslynMcp.Tests/CompileCheckServiceTests.cs:139`

## Acceptance

- [ ] `IsolatedWorkspaceTestBase` exposes a single helper that sets/clears the copied solution's `<Project>` entries (add, replace, remove-all), using `IsolatedWorkspaceScope.SolutionPath` rather than a re-composed `"SampleSolution.slnx"` literal.
- [ ] `AddProjectToCopiedSolution`, `ScaffoldingFirstTestFileTests.ReplaceSolutionFile`, and the `CompileCheckServiceTests` zero-project test setup all route through it; no test hand-rolls XDocument `.slnx` surgery.

## Evidence

Traced during code-quality review of `compile-check-buildhint-whitespace-discriminator-mismatch`: three separate inline variants of "rewrite the copied `.slnx` project list" exist in the test assembly — `IsolatedWorkspaceTestBase.AddProjectToCopiedSolution` (line 36-40) does the `XDocument` add, `ScaffoldingFirstTestFileTests.ReplaceSolutionFile` (line 368) does a full rewrite but is private to that class, and the new `CompileCheckServiceTests` test hand-rolls the remove-all case, re-deriving `"SampleSolution.slnx"` as a literal instead of using `IsolatedWorkspaceScope.SolutionPath` (which the scope already exposes verbatim, and which `ScaffoldingFirstTestFileTests` already uses correctly).

## Context

Spin-off from the `compile-check-buildhint-whitespace-discriminator-mismatch` row's code-quality review (top-n-remediation run 20260810T233007Z).
