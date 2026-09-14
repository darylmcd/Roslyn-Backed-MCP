---
category: Fixed
---

- **Fixed:** Type-move previews reject ambiguous and nested declarations, preserve enclosing namespace and import scopes without guessing generic collection imports or rewriting accessibility, and propagate cleanup cancellation and failures before issuing a preview. Closes `type-move-nested-and-ambiguous-selection`, `type-move-namespace-import-preservation`, and `type-move-unused-using-failure-observability`.
