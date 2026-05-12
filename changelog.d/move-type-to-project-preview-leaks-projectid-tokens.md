---
category: Fixed
---

- **Fixed:** `move_type_to_project_preview` (and the related `extract_interface_cross_project_preview` / `dependency_inversion_preview`) emitting raw Roslyn `ProjectId` tuple strings in circular-dependency error messages. The error message now reads "Adding project reference from 'ProjectA' to 'ProjectB' would create a circular dependency." using the human-readable project names the caller supplied.
