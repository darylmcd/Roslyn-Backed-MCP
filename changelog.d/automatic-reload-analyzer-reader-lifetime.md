---
category: Fixed
---

- **Fixed:** Automatic stale-workspace reload now runs under the per-workspace writer lock (and the restore reload inside `workspace_load` takes the writer too, without holding it across `dotnet restore`), so an in-flight analyzer or generator reader keeps its dependency resolution until it releases; concurrent stale callers collapse into one reload, and a nested same-workspace call never waits on its own enclosing reader. Closes `automatic-reload-analyzer-reader-lifetime`.
