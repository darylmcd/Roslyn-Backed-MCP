---
category: Fixed
---

- **Fixed:** `set_editorconfig_option` (and `set_diagnostic_severity`) appending a duplicate key line when the target `.editorconfig` already contains the same key. Key-matching now normalizes whitespace around `=` so both `key = value` and `key=value` variants are recognized and replaced in place. Repeated calls with the same key+value are now a no-op leaving the file hash unchanged. Closes gh #735.
