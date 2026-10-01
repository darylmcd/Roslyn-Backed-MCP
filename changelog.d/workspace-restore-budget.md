---
category: Fixed
---
- **Fixed:** `workspace_load`/`workspace_reload` auto-restore now runs under a dedicated `ROSLYNMCP_RESTORE_TIMEOUT_SECONDS` budget clamped to the remaining request deadline minus a reload reserve, so a slow restore can no longer starve the follow-up reload.
