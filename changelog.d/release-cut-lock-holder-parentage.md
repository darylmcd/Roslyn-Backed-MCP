---
category: Maintenance
---

- **Maintenance:** The `/release-cut` Step 6b and `/update` Step 3 lock-holder guidance now identifies the tool-store holder by `ParentProcessId` instead of assuming it is the current session's own MCP server. The v4.2.0 cut found all three holders parented to `codex.exe`, where restarting Claude Code releases nothing because its server runs from the Layer 2 `dnx` pin. Both skills now cover the other-agent case and the respawn race that defeats a stop-then-update run split across separate calls.
