---
category: Fixed
---

- **Fixed:** `WorkspaceCloseDrainTests` used hardcoded Windows paths (`C:\repo\...`) that caused `Path.GetDirectoryName` to return an empty string on Linux, silently skipping the drain call and failing the assertion; paths replaced with `Path.Combine(Path.GetTempPath(), ...)` so the test is cross-platform. This was blocking NuGet publish for v1.37.0 and v1.38.0.
