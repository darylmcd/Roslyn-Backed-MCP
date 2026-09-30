---
category: Fixed
---

- **Fixed:** Cross-project refactoring previews (`move_type_to_project_preview`, `extract_interface_cross_project_preview`, `extract_and_wire_interface_preview`, `dependency_inversion_preview`) now return their refusal reasons (project-reference cycle, existing target file named relative to the target project, unresolved type or project) instead of the generic fallback text. Closes `cross-project-refactoring-refusals-public-message`.
