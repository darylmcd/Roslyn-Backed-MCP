---
category: Fixed
---

- **Fixed:** `member_hierarchy` now returns the standard `{error, category: "NotFound", message}` envelope when the locator resolves no symbol, instead of a bare JSON `null` that was ambiguous to callers (no-result vs failure). The message names the locator field supplied and echoes the unresolved value, matching the `symbol_relationships` / `symbol_info` convention. Closes `member-hierarchy-bare-null`.
