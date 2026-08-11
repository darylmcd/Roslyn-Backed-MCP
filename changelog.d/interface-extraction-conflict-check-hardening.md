---
category: Fixed
---

- **Fixed:** `InterfaceExtractionService`'s same-name conflict check is now covered by a regression test and its per-project catch narrows from bare `Exception` to the compilation-retrieval exceptions actually expected, logging at Warning instead of Debug — a cache/compilation failure no longer silently degrades the conflict check to a no-op (`interface-extraction-conflict-check-hardening`).
