---
category: Fixed
---

- **Fixed:** Removing or moving a directory above a loaded workspace root now retires that workspace (and every other workspace beneath that directory) instead of leaving a session whose files no longer exist, while workspaces elsewhere stay watched.
