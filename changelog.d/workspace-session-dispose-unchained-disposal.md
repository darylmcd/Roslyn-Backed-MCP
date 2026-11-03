---
category: Fixed
---

- **Fixed:** Workspace session and loader cleanup now attempts workspace, analyzer lease, and session load-lock disposal in order even when an earlier step fails. Load failures retain their original exception while cleanup faults are reported. Closes `workspace-session-dispose-unchained-disposal`.
