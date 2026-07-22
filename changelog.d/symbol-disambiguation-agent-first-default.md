---
category: Fixed
---

- **Fixed:** `symbol_search`, `find_references`, and `go_to_definition` no longer open a blocking MCP `elicitation/create` operator prompt by default when a query or metadata name resolves to multiple candidates — the calling agent now receives the structured, paginated candidate list (with stable `symbolHandle` values) directly, matching the documented tool response shape. The previous operator-picker behavior is preserved as an explicit opt-in via the new `allowElicitation` parameter (default `false`).
