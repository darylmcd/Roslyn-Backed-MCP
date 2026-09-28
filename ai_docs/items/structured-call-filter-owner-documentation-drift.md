# structured-call-filter-owner-documentation-drift — Correct structured-call ownership comments

**row:** `structured-call-filter-owner-documentation-drift` · **pri:** `Low` · **size:** `M` · **deps:** `structured-call-tool-filter-pipeline-decomposition`

## Anchors

- `src/RoslynMcp.Host.Stdio/Middleware/StructuredCallElicitationCoordinator.cs` — stale statements that the filter retains auto-resolution, auto-load, error-envelope construction, and workspace-load result extraction.
- `src/RoslynMcp.Host.Stdio/Middleware/SolutionDiscoveryHelper.cs` — stale caller/owner reference for omitted-workspace auto-load.
- `tests/RoslynMcp.Tests/StructuredCallToolFilterAutoLoadTests.cs` — stale test comment naming the filter rather than the responsible collaborator.

## Acceptance

- [ ] Update ownership comments to name `StructuredWorkspaceResolver` for workspace auto-resolution/auto-load and `StructuredResultProjector` for terminal error construction after the pipeline decomposition lands.
- [ ] Update the workspace-load result extraction reference to its live owner, without changing behavior or broadening the refactor.
- [ ] Update the test comment to identify the live owner of ambiguous auto-load fast-fail behavior.
- [ ] A targeted source/reference check or existing focused structured-call regression confirms no stale owner claims remain in these anchors.

## Evidence

- 2026-09-10 cold review of `structured-call-tool-filter-pipeline-decomposition` found these comments still describe the pre-decomposition ownership graph. The code refactor intentionally excluded them to preserve its approved file set; this row keeps the stale architecture documentation visible and bounded.
Additional stale owner claim (backlog-remediate 20260926T234932Z follow-up): `src/RoslynMcp.Host.Stdio/Tools/ToolErrorHandler.cs:483` documents `ClassifyAndFormat` as "Facade for the StructuredCallToolFilter", but the filter pipeline (`StructuredResultProjector.cs:79/107/160`) calls `ClassifyError` + `FormatErrorResponse` directly and never uses it. At HEAD 6f31f065 its only src callers are ValidationTools catch blocks, which PR #1659 removes; after that it is test-only. Fix the doc comment (or retire/relocate the method) with the other ownership comments. The sibling stale test comment in ValidationToolsIntegrationTests.cs (~:200, "ClassifyAndFormat convention used elsewhere (e.g. ValidationBundleTools.ValidateRecentGitChanges)") is already deleted by PR #1659 — no action here.
