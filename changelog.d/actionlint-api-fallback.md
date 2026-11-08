---
category: Fixed
---

- **Fixed:** The actionlint validation gate uses the checksum-pinned GitHub API asset when the release download URL fails, so a transient release gateway error does not block CI.
