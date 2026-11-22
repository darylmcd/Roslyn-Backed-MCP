---
category: Added
---

- **Added:** `change_type_namespace_preview` (experimental) now relocates enum and delegate types, in place, into a new block of the same file, or to a new file, and rewrites their consumers like any other type, instead of refusing them with `type-kind Enum/Delegate not supported`; a nested enum or delegate still refuses as a nested type, and a generic delegate that shares its name with another type in the namespace reports the match as ambiguous. `move_type_to_project_preview` and `move_type_to_file_preview` follow in their own rows. Part of `relocation-support-enum-delegate-types`.
