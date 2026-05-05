---
category: Fixed
---

- **Fixed:** `apply_with_verify` (and `apply_text_edit`/`apply_multi_file_edit verify=true`) no longer false-positive-rolls-back when a pre-existing diagnostic's message text drifts on the post-apply build path. The diagnostic-identity helper used by both verify entry points now keys on `id|file|line` (via the new shared `DiagnosticIdentitySet` helper) instead of `id|file:line:col|message` — message-text shifts and column flips on the same pre-existing diagnostic no longer mark it as a new error. The `(post.Except(pre)).Any()` rollback trigger condition is unchanged; only the identity format moved. Closes `apply-with-verify-diff-not-counts`.
