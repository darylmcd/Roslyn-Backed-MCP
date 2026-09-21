---
category: Changed — BREAKING
---

- **Changed — BREAKING:** An unknown or never-loaded `workspaceId` now returns `category=WorkspaceNotFound` (exception `WorkspaceNotFoundException`) instead of `NotFound`; consumers branching on `category` must handle it. Symbol, file and metadata-name misses stay `NotFound`. Supersedes the earlier typo-stays-`NotFound` note.
