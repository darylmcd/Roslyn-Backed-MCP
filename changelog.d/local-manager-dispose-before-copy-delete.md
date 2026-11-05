---
category: Fixed
---

- **Fixed:** Local workspace-manager tests now await shutdown before deleting their copied solution roots, and clean up each acquired root after partial setup failures. Closes `test-local-manager-dispose-before-copy-delete`.
