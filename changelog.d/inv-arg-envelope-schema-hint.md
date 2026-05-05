---
category: Changed
---

- **Changed:** `InvalidArgument` error envelopes now carry a `schemaHint` field naming the failing parameter's type and (when known) its description, sourced from a reflected tool-parameter index. Cold-context callers and parallel-mode subagents can re-call without round-tripping through `server_info`. When the failing parameter cannot be resolved (e.g. a JSON deserialization error before binding picks one), the hint falls back to a tool-level signature listing all user-facing parameters; when the tool itself is unknown, the field is omitted rather than emitted as `null`. Closes `inv-arg-envelope-schema-hint`.
