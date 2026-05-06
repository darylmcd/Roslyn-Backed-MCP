---
category: Added
---

- **Added:** `symbol_search`, `find_references`, and `go_to_definition` invoke MCP `elicitation/create` when the resolved name is ambiguous (overloads, partial classes, inherited members) and the client declares the `elicitation` capability. The agent is asked to pick a candidate via a labeled select-from-N prompt; clients without elicitation continue to receive the existing disambiguation-list response (purely additive, no breaking change). Closes `elicit-disambiguation-on-multi-symbol-resolve`.
