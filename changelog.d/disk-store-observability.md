---
category: Maintenance
---

- **Maintenance:** `PersistentCompositeStorage` and `WorkspaceCacheStore` now accept an optional `ILogger<T>` (injected via DI in both `ServiceCollectionExtensions`); corrupt-entry drops and best-effort delete failures are logged at `Debug` level instead of silently swallowed.
