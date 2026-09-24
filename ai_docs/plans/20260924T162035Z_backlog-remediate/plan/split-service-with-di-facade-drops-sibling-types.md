| Field | Content |
|---|---|
| Route | `direct` |
| Diagnosis | The facade builder (`src/RoslynMcp.Roslyn/Services/SymbolRefactorService.cs:843`) builds `SyntaxFactory.CompilationUnit().WithUsings(usings)` + `WrapInNamespace(classDecl)`, normalizes and writes it over the original file — every other type, the base list and constants/fields not partitioned are dropped. Confirms the row. |
| Approach | Row Acceptance verbatim: (1) Splitting a service whose file also declares other types/interfaces keeps those declarations, base list and constants intact; (2) Preview output compiles (compile_check 0 new errors) for a multi-type source file; (3) Regression test covers a file with an interface + 2 classes. Regression test first in `tests/RoslynMcp.Tests/CompositeSplitServiceDiPreviewTests.cs`. |
| Scope | Production files (1): `src/RoslynMcp.Roslyn/Services/SymbolRefactorService.cs`. Test files (1): `tests/RoslynMcp.Tests/CompositeSplitServiceDiPreviewTests.cs`. No deletions. |
| Tool policy | `edit-only` |
| Estimated context cost | 35000 |
| Risks | Rewrite should replace only the original type declaration node within the original root (keeping sibling types, base list, non-method members) rather than synthesizing a fresh compilation unit; avoid whole-file `NormalizeWhitespace` so unrelated formatting survives. Fanout probe: private helper in one service; 0 external ripple. |
| Validation | Targeted `test_run --filter "FullyQualifiedName~<TestClass>"` per edit; full addenda `ci_equivalent` (`just ci`) before PR. |
| Performance review | N/A — correctness fix, no hot-path changes. |
| CHANGELOG category | Fixed |
| CHANGELOG entry (draft) | `split_service_with_di_preview` no longer deletes other types, the base list, or constants declared in the same file as the split service. |
| Backlog sync | Close rows: [split-service-with-di-facade-drops-sibling-types]. Mark obsolete: []. |
