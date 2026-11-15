---
category: Fixed
---

- **Fixed:** Drain admitted workspace operations during shutdown and preserve their cancellation and deadlines until they finish. The host now completes in-flight missing-root retirement before releasing the execution gate. Closes `workspace-execution-gate-drain-on-dispose` and `workspace-provider-retirement-shutdown-order`.
