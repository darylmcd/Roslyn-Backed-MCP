---
category: Changed
---

- **Changed:** `apply_composite_preview` catalog summary and tool `[Description]` now lead with a `DESTRUCTIVE` marker so agents reading `discover_capabilities` (or the tool-schema description) see the warning before invoking. Tool name is unchanged (rename to `composite_apply` deferred — would require a catalog version bump). Closes `apply-composite-preview-name-friction` (PR #471).
