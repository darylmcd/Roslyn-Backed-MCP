---
category: Fixed
---

- **Fixed:** `split_class_preview` no longer leaves whitespace-only orphan-indent lines where removed members used to live. `TriviaNormalizationHelper.RemoveOrphanIndentTrivia` is a token-walking rewriter that strips `WhitespaceTrivia` sandwiched between two `EndOfLineTrivia` items, operating on syntax trivia (not token text) so verbatim string literals with whitespace-only lines are left intact (`split-class-preview-orphan-indent`).
