---
category: Changed
---

- **Changed:** The `revert_last_apply` tool description now leads with a loud **SINGLE-SLOT LIFO** warning — it reverts only the most recent apply and reports "No operation to revert" even when earlier applies remain in `workspace_changes` — and cross-points to `revert_apply_by_sequence` (keyed by the `workspace_changes` sequence number) as the path to revert an earlier apply. No behaviour change; clarifies a sharp footgun for MCP clients. Closes `revert-last-apply-single-slot-doc-warning`.
