---
category: Added
---

- **Added:** Added a checksum-pinned, repository-owned `actionlint` gate wired into `just ci`, so a malformed GitHub Actions `if:`/`${{ }}` expression is caught locally before push instead of only after.
