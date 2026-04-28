---
category: Fixed
---

- **Fixed:** `symbol_info` not-found errors now name the locator field that was supplied (`metadataName` / `filePath:line:col` / `symbolHandle`) instead of the generic *"No symbol found at the specified location"* text. Branching logic placed in a new `SymbolLocatorFactory` so the message differentiation is reusable; sibling navigation tools (`symbol_relationships`, `goto_type_definition`, `find_consumers`, `AnalysisTools` resolvers) still return the legacy text and are tracked for follow-up via `navigation-tools-misnamed-locator-error`. Closes `symbol-info-not-found-message-locator-vs-location` (PR #474).
