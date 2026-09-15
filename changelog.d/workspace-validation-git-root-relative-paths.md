---
category: Fixed
---

- **Fixed:** Validation resolves Git changes against the repository root for nested solutions. Shared PowerShell test execution now bounds post-exit output draining and observes cancelled readers, with owned descendant cleanup in regression tests. Closes `workspace-validation-git-root-relative-paths` and `pwsh-script-runner-post-exit-drain-budget`.
