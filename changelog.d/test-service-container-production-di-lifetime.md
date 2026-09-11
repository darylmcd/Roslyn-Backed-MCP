---
category: Fixed
---

- **Fixed:** Make the service provider the sole owner of DI-created workspace file watchers, and align the shared test container with the production Roslyn composition root. Closes `test-service-container-production-di-lifetime` and `workspace-manager-file-watcher-disposal-ownership`.
