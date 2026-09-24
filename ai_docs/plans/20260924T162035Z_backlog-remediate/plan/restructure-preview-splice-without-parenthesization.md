| Field | Content |
|---|---|
| Route | `direct` |
| Diagnosis | `PlaceholderSubstituter.VisitIdentifierName` (`src/RoslynMcp.Roslyn/Services/RestructureService.cs:410`) returns `captured.WithTriviaFrom(node)` with no parenthesization, so a binary capture spliced into a higher-precedence context changes semantics (`count + count * 3`). Confirms the row. |
| Approach | Row Acceptance verbatim: (1) Pattern `__a__ + 1` → goal `__a__ * 3` applied to `count + 1` style input where __a__ binds a binary expression yields a parenthesized result; (2) Regression test asserts precedence is preserved for binary/conditional captures. Regression test first in `tests/RoslynMcp.Tests/RestructureServiceTests.cs`. |
| Scope | Production files (1): `src/RoslynMcp.Roslyn/Services/RestructureService.cs`. Test files (1): `tests/RoslynMcp.Tests/RestructureServiceTests.cs`. No deletions. |
| Tool policy | `edit-only` |
| Estimated context cost | 30000 |
| Risks | Wrap expression captures in `ParenthesizedExpression` annotated with `Simplifier.Annotation` and run `Simplifier.ReduceAsync` (or equivalent) on the result so redundant parens vanish; non-expression captures (statements, types) untouched. Verify existing RestructureServiceTests expectations do not start showing redundant parens. Fanout probe: nested private class; 0 external ripple. |
| Validation | Targeted `test_run --filter "FullyQualifiedName~<TestClass>"` per edit; full addenda `ci_equivalent` (`just ci`) before PR. |
| Performance review | N/A — correctness fix, no hot-path changes. |
| CHANGELOG category | Fixed |
| CHANGELOG entry (draft) | `restructure_preview` now parenthesizes captured expressions when substituting placeholders so operator precedence is preserved. |
| Backlog sync | Close rows: [restructure-preview-splice-without-parenthesization]. Mark obsolete: []. |
