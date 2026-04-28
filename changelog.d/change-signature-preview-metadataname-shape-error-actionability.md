---
category: Fixed
---

- **Fixed:** `change_signature_preview` rejection of parenthesized `metadataName` (e.g. `Foo.Bar.Baz(string)`) now names the shape mismatch and points at the supported form (bare method name + position, or `symbolHandle` from `symbol_search`) instead of the vague `"requires a method symbol; resolved null"`. Closes `change-signature-preview-metadataname-shape-error-actionability` (PR #467).
