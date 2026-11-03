---
category: Fixed
---

- **Fixed:** Extract-type member modifier rewriting now uses one dispatch for supported method, property, field, and event declarations, preserves leading modifier trivia, and fails explicitly for unsupported syntax kinds. Closes `extract-type-member-modifier-dispatch-duplication`.
