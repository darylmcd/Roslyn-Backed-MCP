---
category: Fixed
---

- **Fixed:** `get_di_registrations` no longer over-counts dead registrations for intentional multi-registration patterns. Service types injected via `IEnumerable<T>` / `IReadOnlyList<T>` / `IList<T>` / `T[]` constructor or property parameters are excluded from the override-chain output because `GetServices<T>()` returns every registration for those consumers. Also fixed factory-lambda implementation-type resolution: `AddSingleton<IFoo>(sp => sp.GetRequiredService<FooImpl>())` (and the `GetService<T>` variant) now reports `FooImpl` as the winning implementation type rather than the opaque `"factory"` sentinel; lambdas without a recognizable service-locator call still fall back to `"factory"`. Fixes gh #768 §13.16.
