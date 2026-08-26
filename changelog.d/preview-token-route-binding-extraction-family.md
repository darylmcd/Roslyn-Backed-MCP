---
category: Fixed
---

- **Fixed:** the four extraction/move apply routes (`extract_method_apply`, `extract_interface_apply`, `extract_type_apply`, `move_type_to_file_apply`) now bind to their producer families and reject a preview token minted by a different `*_preview` before any workspace mutation, with an error naming the token's real producer and the route it should be redeemed through. Adds the matching `PreviewKind` members plus the missing `extract_interface_preview` → `extract_interface_apply` catalog pairing, so the mismatch message stays factual instead of falling back to the generic `apply_with_verify` advice. Tokens with unrecorded provenance still redeem unchanged, and `apply_with_verify` remains producer-agnostic by design.
