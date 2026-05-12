---
category: Fixed
---

- **Fixed:** `get_editorconfig_options` returning stale cached values for known keys (e.g. `indent_size`, `csharp_style_var_for_built_in_types`) immediately after `set_editorconfig_option` writes a new value. The disk-parsed value is now authoritative when the `.editorconfig` file is present, overriding the Roslyn workspace snapshot for any key the snapshot had cached before the write.
