---
category: Maintenance
---

- **Maintenance:** The atomic-writer persistent-reader test now bounds its retry wait at 30 s instead of 5 s, because full-suite load stretched its ~2.4 s retry budget to ~7 s and failed local and hosted gates; test-only, no shipped behavior change.
