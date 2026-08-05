---
category: Fixed
---

- **Fixed:** `apply_text_edit`, `apply_multi_file_edit`, `set_editorconfig_option`, and `apply_project_mutation` now capture byte-exact pre-mutation snapshots via `FileSnapshotDto.FromExistingBytes` before mutating disk, so `revert_last_apply` restores UTF-8-BOM and UTF-16 fixtures byte-for-byte on all four direct-mutation paths (previously only the refactoring file-set snapshot path had this fix).
