---
category: Maintenance
---

- **Maintenance:** Extracted `get_source_text` request validation and wire projection into `SourceTextRequestProjection`, reducing `WorkspaceTools.GetSourceText` to endpoint orchestration. No change to the tool's parameter schema, error envelopes, line counts, clamp behavior, or truncation marker.
