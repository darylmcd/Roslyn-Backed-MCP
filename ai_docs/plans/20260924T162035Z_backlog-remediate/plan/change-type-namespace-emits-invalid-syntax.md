| Field | Content |
|---|---|
| Route | `direct` |
| Diagnosis | `MoveTypeIntoNewNamespaceInSameFile` (`src/RoslynMcp.Roslyn/Services/NamespaceRelocationService.cs:371`) builds `SyntaxFactory.NamespaceDeclaration(ParseName(toNamespace))` with no keyword/brace trivia and no formatting, yielding `namespaceSampleLib.Shapes{`. Confirms the row; `BuildMovedTypeCompilationUnit` already sets explicit keyword trivia for the file-scoped case. |
| Approach | Row Acceptance verbatim: (1) Preview on a file-scoped-namespace file produces compilable output (apply_with_verify status=applied); (2) Regression test covers file-scoped and block-scoped sources. Regression test first in `tests/RoslynMcp.Tests/NamespaceRelocationTests.cs`. |
| Scope | Production files (1): `src/RoslynMcp.Roslyn/Services/NamespaceRelocationService.cs`. Test files (1): `tests/RoslynMcp.Tests/NamespaceRelocationTests.cs`. No deletions. |
| Tool policy | `edit-only` |
| Estimated context cost | 25000 |
| Risks | Give the appended block namespace explicit trivia (or `NormalizeWhitespace` on the appended node only / Formatter annotation) and verify the relocated type body keeps its indentation. Check `RewriteNamespaceName` path for the same issue. Fanout probe: private static helpers; 0 external ripple. |
| Validation | Targeted `test_run --filter "FullyQualifiedName~<TestClass>"` per edit; full addenda `ci_equivalent` (`just ci`) before PR. |
| Performance review | N/A — correctness fix, no hot-path changes. |
| CHANGELOG category | Fixed |
| CHANGELOG entry (draft) | `change_type_namespace_preview` now emits valid namespace syntax when the relocated type stays in a file with other types (previously `namespaceX.Y{`). |
| Backlog sync | Close rows: [change-type-namespace-emits-invalid-syntax]. Mark obsolete: []. |
