---
category: Fixed
---

- **Fixed:** `semantic_grep` tool description now explicitly documents that the `identifiers` scope tokenizes on C# lexer boundaries — dotted member-access expressions (e.g. `Task.Run`) are multiple tokens and will not match as a single pattern. The updated description recommends two recoverable workarounds: (a) two separate identifier-scope calls intersected client-side by (filePath, line), or (b) `scope="all"` with a prose-fragment pattern when the dotted text also appears in a comment or string literal. Added regression tests verifying both the zero-hit gap and the documented intersection workaround. Closes `semantic-grep-dotted-identifiers-tokenization-docs-gap`. Fixes gh #768 §13.15.
