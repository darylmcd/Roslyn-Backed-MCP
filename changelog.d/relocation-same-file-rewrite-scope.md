---
category: Fixed
---

- **Fixed:** `change_type_namespace_preview` without a destination file now writes the full destination namespace for a type in a nested namespace, keeps conditional-compilation directive pairs whole under every define when it moves one type out of a file, keeps namespace-scoped imports (aliases included) bound for the moved type, and escapes keyword segments in the destination namespace. Behaviour change / migration note: a same-file relocation out of a nested source namespace now lands in exactly the requested namespace (`Outer.Original` to `Destination` previously produced `Outer.Destination`, now `Destination`); callers that relied on the old nested result must pass the full target namespace (`Outer.Destination`) (`relocation-same-file-nested-destination-namespace`, `relocation-same-file-directive-ownership`, `relocation-same-file-preserve-declaration-imports`).
