---
category: Fixed
---

- **Fixed:** `extract_type_preview` now emits the extracted type under the source type's full containing namespace for types in nested namespaces (previously only the innermost namespace name was copied, so an instance extraction was falsely refused with compiler errors and an unreferenced static extraction landed in the wrong namespace). Closes `extract-type-nested-namespace-identity`.
