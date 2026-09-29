| Field | Content |
|---|---|
| Route | direct |
| Diagnosis | `src/RoslynMcp.Roslyn/Services/StringLiteralReplaceService.cs:135-154` rewrites every allowed string literal, including the initializer of the constant named by the replacement expression; that constructs a self-reference and CS0110. |
| Approach | Identify the replacement target symbol using the document semantic model and skip only its own declaration initializer; continue replacing other matching literals in arguments, assignments, and initializers. Add a red-first test with a constant target and other occurrences. |
| Scope | Production 1: `src/RoslynMcp.Roslyn/Services/StringLiteralReplaceService.cs`. Tests 1: `tests/RoslynMcp.Tests/StringLiteralReplaceServiceTests.cs`. No deletions. Fragment: `changelog.d/replace-string-literals-const-self-reference.md`. |
| Tool policy | edit-only |
| Estimated context cost | 30000 |
| Risks | Preserve replacement of unrelated constants' initializers and syntax-only previews when target binding fails; avoid resolving replacement text by string equality alone. No refactor-shaped fanout. |
| Validation | Red-first constant self-reference regression, existing string-literal tests, `just ci`. |
| Performance review | N/A — correctness fix, no hot-path changes. |
| CHANGELOG category | Fixed |
| CHANGELOG entry (draft) | Avoid self-references when replacing literals with a constant. |
| Backlog sync | Close rows: [replace-string-literals-const-self-reference]. Mark obsolete: []. |
