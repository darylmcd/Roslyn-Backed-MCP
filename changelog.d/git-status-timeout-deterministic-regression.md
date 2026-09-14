---
category: Fixed
---

- **Fixed:** Make the git-status timeout regression wait for cancellation instead of racing a 1 ms timer against real Git; retain the unknown-scope verdict and normal dirty-file control. Closes `git-status-timeout-deterministic-regression`.
