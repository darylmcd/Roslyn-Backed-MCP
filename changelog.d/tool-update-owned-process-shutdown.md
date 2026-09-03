---
category: Fixed
---

- **Fixed:** `just tool-update` failing with an access-denied error when an owned Layer 1 `roslynmcp` process holds the tool-store lock; the update now stops only an owned process (by PID + start time, matched by image path under the tool store) and fails closed naming the holder when it cannot attribute the lock.
