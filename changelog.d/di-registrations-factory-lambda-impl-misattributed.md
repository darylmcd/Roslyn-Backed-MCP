---
category: Fixed
---

- **Fixed:** `get_di_registrations` reports the constructed type for factory lambdas like `sp => new Foo(sp.GetRequiredService<Bar>())` (expression or block body) instead of the first resolved dependency; direct `GetRequiredService<T>()` / `GetService<T>()` forwards still report `T`, and other returned shapes fall back to `factory`. Closes `di-registrations-factory-lambda-impl-misattributed`.
