---
category: Fixed
---

- **Fixed:** `change_type_namespace_preview` now rewrites a consumer's imports per namespace scope from bound reference sites instead of judging the whole file by its first namespace and by unbound identifier spelling: a file with several namespaces gets the destination import exactly where a scope needs it, an unrelated same-named type no longer gains a spurious (ambiguous) `using`, imports declared inside a namespace and `global using` directives of the emptied namespace are rewritten, and a file that imports the emptied namespace without referencing the type no longer keeps a dangling `using`. Closes `namespace-relocation-consumer-scope-identity`.
