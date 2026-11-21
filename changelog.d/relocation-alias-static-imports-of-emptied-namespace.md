---
category: Fixed
---

- **Fixed:** `change_type_namespace_preview` now retargets a `using A = Old;` alias (compilation-unit or namespace-scoped) to the destination when relocating the last type empties `Old`, instead of leaving it dangling (CS0246); an alias of a namespace that still exists (for example through a child namespace) is left alone. An alias or `using static` directive is never treated as the plain import of the old namespace. Closes `relocation-alias-static-imports-of-emptied-namespace`.
