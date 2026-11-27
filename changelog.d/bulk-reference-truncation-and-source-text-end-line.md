---
category: Fixed
---

- **Fixed:** Report the actual last returned source line in `get_source_text` (`returnedEndLine`) when `maxChars` truncates a slice, and share `find_references`'s deterministic `(filePath, startLine, startColumn)` ordering in `find_references_bulk` so `maxItemsPerSymbol=N` aligns with `find_references(offset=0, limit=N)`. Closes `bulk-reference-truncation-and-source-text-end-line`.
