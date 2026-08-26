---
category: Maintenance
---

- **Maintenance:** Standardized `workspaceId` and `filePath` parameter descriptions on the workspace-warm, workspace-drift, undo, and editorconfig tool surfaces to the canonical short form, and replaced the per-slice canonicalization guards with an assembly-wide ratchet that rejects any third phrasing and monotonically lowers the legacy-form ceiling. Load-bearing parameter guidance (JSON-array format contracts, `workspace_changes` sequence semantics, editorconfig key/value examples, optional-`workspaceId` auto-resolution contracts) is unchanged.
