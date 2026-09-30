---
category: Fixed
---

- **Fixed:** Formatter baseline timeout diagnostics now retain child process IDs that arrive in the drained phase-marker stream after the first asynchronous snapshot, and verify those children exited before reporting cleanup success.
