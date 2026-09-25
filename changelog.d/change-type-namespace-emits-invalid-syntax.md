---
category: Fixed
---

- **Fixed:** `change_type_namespace_preview` now emits valid, formatted namespace syntax when the relocated type stays in a file with other types (previously `namespaceX.Y{`). A file-scoped source namespace is converted to block form so the file never mixes file-scoped and block declarations (CS8955), and the moved type's doc comment is no longer duplicated in the source namespace.
