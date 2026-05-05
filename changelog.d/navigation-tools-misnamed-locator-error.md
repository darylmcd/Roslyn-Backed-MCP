---
category: Fixed
---

- **Fixed:** navigation tools `callers_callees`, `find_consumers`, and the `SymbolTools` resolver now emit a locator-aware "no symbol found" message that names the field the caller actually supplied (`filePath:line:column`, `symbolHandle`, or `metadataName`) instead of the legacy literal `"No symbol found at the specified location"`. Matches the fix that landed for `symbol_info` in PR #474. Closes `navigation-tools-misnamed-locator-error`.
