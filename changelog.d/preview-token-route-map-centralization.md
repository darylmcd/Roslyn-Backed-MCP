---
category: Maintenance
---

- **Maintenance:** Centralized the preview-token `PreviewKind` → (`*_preview` tool, `*_apply` route) mapping in `ToolDispatch`, sourcing the apply-route half from `ServerSurfaceCatalog.PreviewApplyRoutes` instead of a third and fourth hand-maintained copy, and removed the redundant `applyRoute` parameter from `ApplyByTokenAsync`. An unmapped concrete `PreviewKind` now fails loudly at redemption instead of silently reporting the token as coming from "an untagged \*_preview tool" and directing the caller to `apply_with_verify`; a new test pins that every non-`Unspecified` member has a mapping.
