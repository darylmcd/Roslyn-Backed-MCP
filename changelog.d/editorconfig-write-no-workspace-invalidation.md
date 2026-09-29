---
category: Changed — BREAKING
---

- **Changed — BREAKING:** `set_editorconfig_option` now refuses an applicable `.editorconfig` outside the loaded workspace's physical root; edit the ancestor file with an operator-authorized tool or place an applicable file inside the workspace (ADR 0012). In-workspace writes refresh diagnostics and retain server versus external edit attribution. Closes `editorconfig-write-no-workspace-invalidation`, `editorconfig-write-outside-workspace`, and `filewatcher-markstaleifrelevant-stale-precedence-comment`.
