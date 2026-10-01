---
category: Fixed
---
- **Fixed:** `build_workspace` and `build_project` are bounded by `BuildTimeout` instead of the 2-minute gate timeout, release the workspace lock during the build, and report `commandDurationMs` and `workspaceChangedDuringRun`.
