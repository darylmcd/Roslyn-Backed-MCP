---
category: Fixed
---

- **Fixed:** Test fixture constructors now remove owned roots when file creation fails and preserve cleanup errors. Workspace cache and output-type tests also retain body failures alongside workspace close and fixture cleanup failures. Closes `project-fixture-construction-failure-leaks-root` and `workspace-cache-test-close-masks-primary`.
