---
category: Added
---

- **Added:** `semantic_grep(pattern, scope=identifiers|strings|comments|all, projectFilter?) → [{filePath, line, column, tokenKind, snippet}]` MCP tool — token-aware grep over C# code, lets agents avoid false-positive matches inside string literals or comments. Walks `SyntaxTree.DescendantTokens` per workspace document and filters by `SyntaxToken.Kind()`. Capped at 500 hits per call. Registered in the experimental tier; surface count bumped to **167 tools** (111 stable / 56 experimental). Closes `semantic-grep-identifier-scoped-search`.
