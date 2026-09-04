---
category: Fixed
audience: users
---

- **Fixed:** Treat a completed cross-process preview-token claim race as a cache miss while preserving fail-closed handling for unrelated storage faults. Closes `persistent-composite-claim-contention`.
