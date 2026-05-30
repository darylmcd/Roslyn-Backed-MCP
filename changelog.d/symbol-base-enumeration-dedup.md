---
category: Maintenance
---

- **Maintenance:** Collapsed three near-identical base-enumeration methods in `SymbolServiceHelpers` — `EnumerateMethodBases` / `EnumeratePropertyBases` / `EnumerateEventBases` (~28 LOC each, differing only by symbol type and the `Overridden{Method,Property,Event}` accessor) — into one generic `EnumerateOverrideBases<T>` that takes the override accessor as a `Func<T, T?>` selector (C# exposes no common `IOverridableSymbol`, hence the delegate). Removes ~55 LOC of triplicated override + interface-implementation walking so the implicit-interface logic lives in one place. `EnumerateTypeBases` (a different shape) is unchanged; behavior is identical. Closes `symbol-base-enumeration-dedup`.
