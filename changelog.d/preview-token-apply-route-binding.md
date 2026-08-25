---
category: Fixed
---

- **Fixed:** preview tokens are now bound to compatible apply routes. The five named refactoring apply tools (`rename_apply`, `organize_usings_apply`, `format_document_apply`, `code_fix_apply`, `format_range_apply`) reject a preview token minted by a different producer family BEFORE any workspace mutation, with an error naming the token's actual producer and the apply route it should be redeemed through. `apply_with_verify` stays producer-agnostic by design and now documents that supported set explicitly. Tokens whose producer is unrecorded (untagged producers, out-of-tree stores) still redeem unchanged; apply routes outside the refactoring family are not yet bound.
