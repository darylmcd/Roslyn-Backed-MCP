---
category: Added
---

- **Added:** `move_type_to_file_preview` (stable) now moves an enum or delegate type to its own file, keeping its namespace and the usings it needs, instead of refusing it with `type-kind Enum/Delegate not supported`; a nested, file-local or directive-bearing declaration still refuses as before, and the preview and apply response shapes are unchanged. Part of `relocation-support-enum-delegate-types`.
