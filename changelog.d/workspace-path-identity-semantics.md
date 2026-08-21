---
category: Fixed
---

- **Fixed:** workspace loaded-path deduplication now uses the shared platform-aware filesystem comparer, preserving case-insensitive identity on Windows and case-sensitive identity on Unix. Closes `workspace-path-identity-semantics`.
