---
category: Fixed
---

- **Fixed:** `find_implementations` now returns a structured `hint` (telling the caller to source-anchor the query) instead of a bare, possibly-empty result when a `metadataName` resolves to a corlib/BCL interface or abstract root (e.g. `System.IDisposable`). Such metadata-anchored roots bind to a single project's corlib reference, so cross-project implementers could silently drop out and read as `count: 0` — indistinguishable from "no implementers." Mirrors the `find_overrides` corlib-virtual guard (gh #754). The normal (non-corlib, source-anchored) path is unchanged. Closes `find-implementations-corlib-metadataname-zero`.
