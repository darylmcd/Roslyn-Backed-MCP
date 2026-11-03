---
category: Fixed
---

- **Fixed:** Owned test fixture cleanup now retries transient directory deletion faults and reports terminal failures without hiding test body faults, including when one workspace cache teardown root fails before another is removed. Closes `workspace-cache-fixture-cleanup-failure-silent`.
