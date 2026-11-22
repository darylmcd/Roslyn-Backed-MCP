---
category: Added
---

- **Added:** `move_type_to_project_preview` (experimental) now moves enum and delegate types to another project, keeping the declaration's namespace when `preserveNamespace` is set, instead of refusing them with `type-kind Enum/Delegate not supported`; like every kind, it adds the project reference but does not rewrite consumers, and a nested enum or delegate still refuses as a nested type. `move_type_to_file_preview` follows in its own row. Part of `relocation-support-enum-delegate-types`.
