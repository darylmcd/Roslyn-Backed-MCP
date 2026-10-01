---
category: Fixed
---
- **Fixed:** `nuget_vulnerability_scan` is bounded by `VulnerabilityScanTimeout` (5 min) instead of the 2-minute gate timeout, and no longer holds the workspace lock or a throttle slot while `dotnet list package` runs.
