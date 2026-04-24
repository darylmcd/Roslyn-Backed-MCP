---
category: Added
---

- **Added:** `find_references_bulk` now supports `summary` (nulls `previewText`) and `maxItemsPerSymbol` (default 100, validated ≥ 1) parameters, mirroring the `find_references` summary-mode contract so batched calls no longer overflow the 120 KB MCP payload cap. Each result exposes `referenceCount` (pre-cap total), `returnedCount`, and `truncated` so callers can paginate the tail via `find_references` (`SymbolTools`). (`find-references-bulk-summary-mode`)
