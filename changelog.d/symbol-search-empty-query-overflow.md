---
category: Fixed
---

- **Fixed:** `symbol_search` now rejects empty/whitespace queries with a structured `{count:0, symbols:[], note:"query must be non-empty — ..."}` envelope instead of dumping the full workspace symbol index (70–80 KB on mid-sized solutions). Guard-clause lives in the tool handler so valid queries keep the normal response shape without a `note` field, letting callers disambiguate rejection envelopes from genuine empty-result hits (`symbol-search-empty-query-overflow`).
