| Field | Content |
|---|---|
| Route | direct |
| Diagnosis | `src/RoslynMcp.Roslyn/Services/FixAllService.cs:330-340` passes `AnalyzerFileReference.Display` to `Assembly.LoadFrom`; Display may be a simple name, so package code-fix providers are missed. |
| Approach | Resolve the analyzer reference's actual full file path for provider loading; retain deduplication by canonical path. Add an analyzer-package diagnostic regression that compares `fix_all_preview` with the working `code_fix_preview` path for CA1822. |
| Scope | Production 1: `src/RoslynMcp.Roslyn/Services/FixAllService.cs`. Tests 1: `tests/RoslynMcp.Tests/FixAllServiceIntegrationTests.cs`. No deletions. Fragment: `changelog.d/fix-all-analyzer-assembly-provider-not-found.md`. |
| Tool policy | edit-only |
| Estimated context cost | 30000 |
| Risks | Verify path resolution is safe for non-file analyzer references and preserves provider-load failure reporting. No fanout-shaped API change. |
| Validation | Red-first analyzer-package CA1822 integration test; focused FixAll tests; `just ci`. |
| Performance review | N/A — correctness fix, no hot-path changes. |
| CHANGELOG category | Fixed |
| CHANGELOG entry (draft) | Find analyzer-package code-fix providers when preparing fix-all previews. |
| Backlog sync | Close rows: [fix-all-analyzer-assembly-provider-not-found]. Mark obsolete: []. |
