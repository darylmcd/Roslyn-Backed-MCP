---
category: Fixed
---

- **Fixed:** `format_range_apply` (and other range-scoped) preview tokens now span a bounded workspace-version range, so a single intervening auto-reload between `*_preview` and `*_apply` no longer invalidates the token; apply still verifies the resulting edit against the current version (`PreviewStore` + `IPreviewStore` contract update; `BoundedStore` sweep helper; `WorkspaceManager.LoadIntoSessionAsync` call site). Closes `format-range-apply-preview-token-lifetime`.
