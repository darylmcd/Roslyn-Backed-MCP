---
category: Maintenance
---

- **Maintenance:** `extract_type_preview` same-file consumer rewriting (extracted-symbol discovery, the symbol-identity syntax rewriter, and its conditional-access and constructor-body fail-closed refusals) moved out of `TypeExtractionService` into the internal `TypeExtractionSameFileConsumerRewriter`, with new regression tests pinning generic/explicit-receiver rewriting, both refusals, and that extracted declarations are not rewritten; no behavior, schema, or message change. Closes `type-extraction-same-file-rewriter-decomposition`.
