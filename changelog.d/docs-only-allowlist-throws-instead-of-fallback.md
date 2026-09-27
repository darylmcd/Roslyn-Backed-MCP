---
category: Fixed
---

- **Fixed:** CI: a missing, empty, or stale docs-only test allowlist (`eng/docs-only-test-classes.txt`) now falls back to running the full test suite on shard 0 with a warning instead of failing CI. Closes `docs-only-allowlist-throws-instead-of-fallback`.
