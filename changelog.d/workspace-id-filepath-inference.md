---
category: Fixed
---

- **Fixed:** omitted `workspaceId` calls with multiple loaded workspaces now resolve a unique owner from the loaded Roslyn document index; genuine ambiguity returns bounded, deterministic workspace IDs and loaded paths. Closes `workspace-id-filepath-inference` and fixes #1129.
