---
category: Fixed
---

- **Fixed:** `change_type_namespace_preview` without a destination file now writes the full destination namespace for a type in a nested namespace (`Outer.Inner.Worker` to `Target` yields `Target.Worker`), keeps conditional-compilation directive pairs whole under every define when it moves one type out of a file, and keeps namespace-scoped imports (aliases included) bound for the moved type; keyword segments in the destination namespace are escaped (`relocation-same-file-nested-destination-namespace`, `relocation-same-file-directive-ownership`, `relocation-same-file-preserve-declaration-imports`).
