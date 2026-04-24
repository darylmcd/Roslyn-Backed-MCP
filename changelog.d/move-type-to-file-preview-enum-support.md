---
category: Fixed
---

- **Fixed:** `move_type_to_file_preview` now emits an explicit `type-kind <Kind> not supported` error for enums, delegates, and other unsupported kinds instead of a misleading "Type 'X' not found" message when the symbol resolves but the kind is rejected (`TypeMoveService`). (`move-type-to-file-preview-enum-support`)
