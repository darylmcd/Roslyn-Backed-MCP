---
category: Fixed
---

- **Fixed:** Lock dead-code analysis behavior so Lazy-backed fields and locals read by nested local functions are not reported as removable. Closes `unused-code-analyzer-static-lazy-field-read-safety`. Closes `unused-code-analyzer-captured-local-read-safety`.
