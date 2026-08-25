---
category: Maintenance
---

- **Maintenance:** Trimmed method-level tool descriptions for the server/discovery surface (`server_info`, `server_heartbeat`, `recommend_workflow`, `suggest_refactorings`, `get_prompt_text`) to <=200-char capability statements, relocating the connection state-machine, prompts-tier note, and usage guidance to XML remarks. Cuts the `tools/list` payload for these 5 tools from 2,826 characters to 979 with no capability statement or discriminating trigger dropped.
