---
category: Added
---

- **Added:** `extract_type_preview` refusals (dangling-reference and member-not-found) now carry a structured `blockingDependencies: [{member, reason}]` field alongside the existing prose message, so callers can programmatically retry with a corrected `memberNames` set instead of abandoning the tool.
