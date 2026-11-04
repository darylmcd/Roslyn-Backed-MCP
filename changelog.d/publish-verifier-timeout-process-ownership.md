---
category: Fixed
---

- **Fixed:** Publish and changelog verifier tests now terminate timed-out child processes, wait for exit, and drain both output streams. The shared runner also owns stdin-write failures while retaining the original error. Closes `publish-verifier-timeout-process-ownership`.
