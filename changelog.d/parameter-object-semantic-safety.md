---
category: Fixed
---

- **Fixed:** `parameter_object_preview` now rewrites grouped parameter references in method bodies, preserves compile-time `nameof` values, refuses write/alias and generated-name hazards before issuing a token, rewrites shifted same-file call sites reliably, and directs callers to the real `apply_with_verify` redemption route. Closes `parameter-object-stale-apply-route`.
