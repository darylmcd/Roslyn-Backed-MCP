---
category: Added
---

- **Added:** Preview-token provenance enforcement on the scaffolding apply routes. `scaffold_type_apply` and `scaffold_test_apply` now declare `PreviewKind.FileCreate` as their compatible producer set, so a rename, format, code-fix, or extraction token redeemed through a scaffold apply route is refused with an actionable message naming the correct route, before any workspace mutation. Untagged tokens — including the ones `scaffold_test_batch_preview` mints — stay permissive. `apply_composite_preview` and `apply_project_mutation` remain unbound: their preview stores do not implement `IPreviewStore` and expose no provenance peek.
