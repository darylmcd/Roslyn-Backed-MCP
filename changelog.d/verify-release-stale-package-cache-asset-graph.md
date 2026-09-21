---
category: Fixed
---

- **Fixed:** Release verification's third-party license check now reads only the current solution's restored asset graphs, so switching `NUGET_PACKAGES` roots no longer reports ambiguous nuspec paths. Closes `verify-release-stale-package-cache-asset-graph`.
