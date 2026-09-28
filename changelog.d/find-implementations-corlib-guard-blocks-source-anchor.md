---
category: Fixed
---

- **Fixed:** `find_implementations` anchored at a source position on a corlib interface (e.g. `IDisposable`) now returns the workspace implementers instead of the corlib hint with count 0. The hint still fires for `metadataName` / `symbolHandle` locators, where its source-anchor advice now leads to a working query.
