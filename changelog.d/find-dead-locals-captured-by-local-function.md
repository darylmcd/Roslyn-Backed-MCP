---
category: Fixed
---

- **Fixed:** `find_dead_locals` no longer reports an outer local that a local function assigns and the enclosing method later reads; each local is now judged only by the body that declares it, so a local declared inside a local function is also no longer reported twice. (`find-dead-locals-captured-by-local-function`)
