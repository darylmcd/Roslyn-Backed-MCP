---
category: Fixed
---
- **Fixed:** `workspace_load` / `workspace_reload` with `autoRestore` now run `dotnet restore` through the per-workspace command gate, so it can no longer write `obj/` concurrently with `build_workspace` / `test_run`; time spent waiting for the gate counts against the restore timeout.
