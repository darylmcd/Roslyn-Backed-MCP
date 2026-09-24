| Field | Content |
|---|---|
| Route | `direct` |
| Diagnosis | `PreviewReplaceInvocationAsync` (`src/RoslynMcp.Roslyn/Services/BulkRefactoringService.cs:235`) rewrites every `FindReferencesAsync(oldMethodSymbol)` location, including those inside `newMethodSymbol`'s own declaration, turning a delegating replacement into infinite recursion. Confirms the row. |
| Approach | Row Acceptance verbatim: (1) Replacing Summarize(...) with SummarizeV2(...) where SummarizeV2 delegates to Summarize leaves SummarizeV2's body unchanged; (2) Regression test asserts no callsite inside the replacement symbol is rewritten. Regression test first in `tests/RoslynMcp.Tests/ReplaceInvocationTests.cs`. |
| Scope | Production files (1): `src/RoslynMcp.Roslyn/Services/BulkRefactoringService.cs`. Test files (1): `tests/RoslynMcp.Tests/ReplaceInvocationTests.cs`. No deletions. |
| Tool policy | `edit-only` |
| Estimated context cost | 25000 |
| Risks | Filter reference locations whose span lies within any of `newMethodSymbol.DeclaringSyntaxReferences` (same tree). Totals/perFileCallsites must reflect the filtered set. Fanout probe: single method; 0 external ripple. |
| Validation | Targeted `test_run --filter "FullyQualifiedName~<TestClass>"` per edit; full addenda `ci_equivalent` (`just ci`) before PR. |
| Performance review | N/A — correctness fix, no hot-path changes. |
| CHANGELOG category | Fixed |
| CHANGELOG entry (draft) | `replace_invocation_preview` no longer rewrites call sites inside the replacement method's own body (which produced infinite recursion). |
| Backlog sync | Close rows: [replace-invocation-rewrites-replacement-body]. Mark obsolete: []. |
