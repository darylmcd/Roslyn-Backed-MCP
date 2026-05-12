---
category: Fixed
---

- **Fixed:** `document_symbols` and its alias `get_symbol_outline` now accept either `filePath` OR `symbolHandle` (or `metadataName`), mirroring the locator flexibility of `symbol_info`. Callers can now pivot directly from a `symbol_info` handle to a document outline without an intermediate file-path roundtrip.
