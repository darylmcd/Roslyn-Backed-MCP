---
category: Added
---

- **Added:** `requestedScope`/`actualScope` fields on `compile_check`'s response so callers can programmatically detect when a multi-project `files[]` scope silently widened to a whole-solution compile, without parsing `restoreHint` prose. Existing `restoreHint` text is unchanged. Closes `compile-check-multi-project-fallback-structured-scope`.
