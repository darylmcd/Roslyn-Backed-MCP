---
category: Fixed
---

- **Fixed:** The workspace project-name/path lookup index is now owned by its workspace session and keyed on the exact `Solution` snapshot: close, eviction and manager disposal release its Project/Solution snapshots, a lookup overlapping close can no longer republish a closed session's index, and a reload between a lookup's solution read and its publish can no longer serve old-solution Projects for the new solution. Closes `workspace-project-index-session-lifetime`.
