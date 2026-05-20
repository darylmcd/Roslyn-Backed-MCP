---
category: Fixed
---

- **Fixed:** Added cross-surface regression test guarding against future divergence between `get_msbuild_properties` and `workspace_reload` on `OutputType` for `Microsoft.NET.Sdk.Web` projects (production fix already shipped previously). Fixes gh #769 §13.25.
