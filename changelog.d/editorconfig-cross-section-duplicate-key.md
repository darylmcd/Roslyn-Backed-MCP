---
category: Fixed
---
`set_editorconfig_option` no longer appends a duplicate key when the target key already exists under a different C#-applicable section. The writer previously searched only its canonical `[*.{cs,csx,cake}]` section, so a key living under `[*.cs]` (or `[*]`) was never matched and a second copy was appended — leaving the key present twice (EditorConfig last-wins kept it functional but the file malformed, and `get_editorconfig_options` could report the stale first value). The writer now searches every C#-applicable section the reader enumerates and updates the matching line in place, only appending genuinely-new keys to the canonical section.
