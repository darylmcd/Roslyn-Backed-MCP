---
category: Fixed
---

- **Fixed:** Class splits and other composite previews require unused physical destinations for generated files. Composite apply preserves files created concurrently and publishes new files before editing existing sources. Re-issue pending persisted composite previews after upgrading or rolling back across the format-4 boundary.
