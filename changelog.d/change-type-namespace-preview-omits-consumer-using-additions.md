---
category: Fixed
---

- **Fixed:** `change_type_namespace_preview` omitting `using` additions on consumer files whose namespace is an ancestor of the destination namespace (e.g., consumer in `A.B` when type moves to `A.B.C`). The `IsAmbientTo` helper incorrectly treated ancestor namespaces as having ambient access to descendant namespaces, causing the consumer-side using-directive pass to skip the required `using toNamespace;` addition. Closes gh #749.
