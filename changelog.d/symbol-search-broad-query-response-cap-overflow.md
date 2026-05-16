---
category: Fixed
---

- **Fixed:** `symbol_search` regression (gh #617): broad queries with `limit > ~30` exceeded the MCP inline transport cap (171 KB observed at `limit=100`). Added `summary=true` parameter that drops expensive per-symbol fields (documentation, parameters, baseTypes, interfaces, modifiers, returnType). Lowered server-side hard cap from 200 to 50; callers relying on `limit > 50` were already receiving tool-results-file fallbacks.
