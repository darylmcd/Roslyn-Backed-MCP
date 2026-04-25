---
category: Fixed
---

- **Fixed:** `semantic_search` no longer surfaces the same symbol twice when partial classes, multi-targeted projects, or linked files cause multiple traversals to land on the same `ISymbol`; `CodePatternAnalyzer` now dedupes by canonical `SymbolHandle` across structured / name-substring / token-or-match fallback passes. The advertised `Debug` payload (`parsedTokens`, `appliedPredicates`, `fallbackStrategy`) and per-result `matchKind` (`"structured"` | `"name-substring"` | `"token-or-match"`) — both computed internally but never projected — now appear in the JSON response (`semantic-search-duplicate-results-and-fallback-signal`).
