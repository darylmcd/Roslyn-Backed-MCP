---
category: Maintenance
---

- **Maintenance:** The shadow-sweep live-owner test now re-sweeps for up to 15 s after the owner exits instead of asserting on one immediate sweep, because the lock handle closes a few milliseconds after the cmd.exe group is reported exited; test-only, no shipped behavior change.
