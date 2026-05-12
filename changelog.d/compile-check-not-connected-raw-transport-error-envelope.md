---
category: Fixed
---

- **Fixed:** `compile_check` transport-disconnect errors now emit a structured `category: "Disconnected"` envelope with a `workspace_reload` recovery hint instead of a raw `"Not connected"` string.
