---
category: Fixed
---

- **Fixed:** `change_type_namespace_preview` (when relocating to a new file), `move_type_to_project_preview` and `remove_dead_code_preview` (`removeEmptyFiles`) no longer delete the source file while it still declares an enum, delegate, type in another or nested namespace block or the global namespace, an assembly attribute, or `#if`-disabled declarations; `change_type_namespace_preview` also keeps `using <old namespace>;` in consumers while the retained source file still declares members of that namespace. A sole type in the global namespace is now removed with its file by `move_type_to_project_preview`. Closes `relocation-source-delete-preserves-other-members`.
