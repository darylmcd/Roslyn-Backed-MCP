---
category: Fixed
---

- **Fixed:** `split_service_with_di_preview` generated constructors now reproduce the source constructor exactly: each partition and the facade inject only the fields the source constructor assigned, keep the original parameter types, names, attributes and defaults (an `ILogger<T>` parameter no longer becomes an unresolvable non-generic `ILogger`), keep `?? throw` null guards in source statement order, and no longer demand a DI parameter for a field the constructor never assigned. The split is now refused, with a specific reason, for a service with more than one instance constructor (previously its overloads silently collapsed into one), a copied parameter or guard that names a member only the source type has, and a parameter self-assignment (`x = x;`).
