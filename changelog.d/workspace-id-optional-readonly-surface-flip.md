---
category: Changed
---

- **Changed:** `workspaceId` is now optional (defaults to `null`) on the read-only tools `go_to_definition`, `find_references`, and `document_symbols`. With a single workspace loaded — or one discoverable from the call context — callers may omit it and the server resolves/auto-loads it (see the read-path middleware). Explicit-id callers are unaffected (additive, not a breaking change). This is a pilot subset; the full read-only surface (and `symbol_search`, whose required `query` parameter needs a signature reorder) is a tracked follow-on. Closes `workspace-id-optional-readonly-surface-flip`.
