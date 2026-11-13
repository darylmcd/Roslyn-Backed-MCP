---
category: Fixed
---

- **Fixed:** Undo's workspace version restore now evicts version-keyed caches (compilations, test discovery, DI scans, MSBuild evaluation, NuGet and diagnostic caches) the same way a reload does, so a later apply that reuses a restored version number can no longer serve the reverted apply's cached results. A revert that restored nothing no longer lowers the version.
