---
category: Fixed
---

- **Fixed:** `change_type_namespace_preview`, `move_type_to_project_preview`, `extract_interface_cross_project_preview`, `dependency_inversion_preview` and `extract_and_wire_interface_preview` now refuse an enum or delegate selection with `type-kind Enum not supported` / `type-kind Delegate not supported` (naming the tool and the kinds it accepts) instead of the misleading "not found" text; a genuine miss still reads "not found". The refusal text is built by one helper shared with `move_type_to_file_preview`. Closes `relocation-selected-enum-delegate-reported-not-found`.
