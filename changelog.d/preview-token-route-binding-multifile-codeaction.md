---
category: Fixed
---

- **Fixed:** `preview_multi_file_edit_apply` and `apply_code_action` are now bound to their producer families. Each records a concrete `PreviewKind` when its paired preview tool mints the token and refuses a token minted by a different family BEFORE any workspace mutation, naming the token's actual producer and the apply route it should be redeemed through. `apply_with_verify` stays producer-agnostic by design; tokens whose producer is unrecorded still redeem unchanged.
