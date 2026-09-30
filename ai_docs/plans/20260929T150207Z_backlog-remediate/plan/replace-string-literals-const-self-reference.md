| Field | Content |
|---|---|
| Route | direct |
| Diagnosis | `src/RoslynMcp.Roslyn/Services/StringLiteralReplaceService.cs:135-154` rewrites every allowed string literal, including the initializer of the constant named by the replacement expression; that constructs a self-reference and CS0110. |
| Approach | Parse the replacement expression first. Preserve symbol-free expressions (including literal concatenations) under the current syntax-only contract. For symbol-bearing expressions, resolve referenced symbols and use semantic identity to skip a target const's own declaration initializer across documents; continue replacing other matching literals. If a symbol-bearing target cannot be bound, refuse or safely skip rather than emit an unverified self-reference. Add red-first same/cross-document const tests plus a symbol-free concatenation control. |
| Scope | Production 1: `src/RoslynMcp.Roslyn/Services/StringLiteralReplaceService.cs`. Tests 1: `tests/RoslynMcp.Tests/StringLiteralReplaceServiceTests.cs`. No deletions. Fragment: `changelog.d/replace-string-literals-const-self-reference.md`. |
| Tool policy | edit-only |
| Estimated context cost | 30000 |
| Risks | Preserve replacement of unrelated constants' initializers. Only a symbol-bearing expression requires binding; an unresolved symbol-bearing target must fail closed or prove the rewrite cannot introduce a self-reference. Preserve the audit's valid symbol-free expression case. No refactor-shaped fanout. |
| Validation | Red-first cross-document constant self-reference and unbound-symbol regressions; preserve a `"d" + "ec"` symbol-free replacement; existing string-literal tests; `just ci`. |
| Performance review | N/A — correctness fix, no hot-path changes. |
| CHANGELOG category | Fixed |
| CHANGELOG entry (draft) | Avoid self-references when replacing literals with a constant. |
| Backlog sync | Close rows: [replace-string-literals-const-self-reference]. Mark obsolete: []. |
