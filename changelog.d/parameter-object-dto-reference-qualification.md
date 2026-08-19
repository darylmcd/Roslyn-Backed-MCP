---
category: Fixed
---

- **Fixed:** `parameter_object_preview` emitted an unqualified generated-DTO type name in the rewritten method declaration and at every call site, so when the DTO's namespace differed from the target declaration's or a caller's namespace (explicit `dtoNamespace`, or the cross-project default) the applied edit produced CS0246. The rewriter now emits a `global::`-qualified reference when the DTO namespace is not already in scope for that document, and preserves the unqualified form otherwise.
