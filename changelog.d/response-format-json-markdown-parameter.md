---
category: Added
---

- **Added:** `validate_workspace` accepts `responseFormat: "json" | "markdown"` (default `null` → bit-for-bit identical JSON envelope; `"markdown"` returns a compact summary table with per-diagnostic / per-test rows capped at 20 each). Other summary-shaped tools (`server_info`, `workspace_list`, etc.) will adopt the pattern in follow-up rows. Closes `response-format-json-markdown-parameter`.
