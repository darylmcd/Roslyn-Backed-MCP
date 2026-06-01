---
category: Fixed
---

- **Fixed:** `get_code_actions` now flattens nested Roslyn code-action groups into previewable leaf actions so `preview_code_action` can preview the returned indices. Closes `preview-code-action-nested-notsupported`.
