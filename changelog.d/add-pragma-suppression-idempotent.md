---
category: Fixed
---

- **Fixed:** `add_pragma_suppression` is now idempotent — when the target line is already covered by an active `#pragma warning disable <id>`, it no-ops (returns `EditsApplied: 0`) instead of appending a second identical pragma. Previously a retry (e.g. after an auto-reload) at the same site accumulated duplicate `#pragma warning disable` directives. Coverage detection reuses the same predicate `verify_pragma_suppresses` uses, so the two cannot drift. Closes `add-pragma-suppression-duplicate-on-retry`.
