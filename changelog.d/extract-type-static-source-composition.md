---
category: Fixed
---

- **Fixed:** `extract_type_preview` injects the private composition field and constructor parameter only when an instance member moves. An all-static move (for example a static method out of a `static class`, which was refused with CS0708/CS0710) now leaves the source type's fields and constructors untouched and rewrites callers to `NewType.Member`; a static-only move from an instance class no longer adds a required constructor parameter, and a static-only move from a primary-constructor class no longer hits the primary-constructor refusal. Mixed moves are unchanged. Closes `extract-type-static-source-composition`.
