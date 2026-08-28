---
category: Fixed
---

- **Fixed:** the maintainer-local `/update` skill pinned a plugin-cache file count to a release ("710+ files at 1.29.0") that predated `.claude-plugin/package-allowlist.txt`, so the documented figure read as a truncated cache during a later release cut. It now points at the allowlist as the authoritative set and describes the magnitude qualitatively, rather than restating a count that goes stale whenever a skill is added or removed.
