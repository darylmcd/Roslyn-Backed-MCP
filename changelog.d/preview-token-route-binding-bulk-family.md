---
category: Fixed
---

- **Fixed:** `bulk_replace_type_apply` and `remove_dead_code_apply` now bind to their producer families and refuse a preview token minted by a different `*_preview` before any workspace mutation, with an error naming the token's real producer and the route it should be redeemed through. Adds `PreviewKind.BulkReplaceType` — deliberately shared by `bulk_replace_type_preview` and `replace_invocation_preview`, which redeem through the same apply route — and `PreviewKind.RemoveDeadCode`, and tags all three producers so provenance is enforced in both directions. Tokens with unrecorded provenance (out-of-tree `IPreviewStore` implementers) still redeem unchanged, and `apply_with_verify` remains producer-agnostic by design.
