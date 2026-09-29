---
category: Changed — BREAKING
---

- **Changed — BREAKING:** `set_editorconfig_option` now refuses an applicable `.editorconfig` outside the loaded workspace's physical root; edit the ancestor file with an operator-authorized tool or place an applicable file inside the workspace (ADR 0012). Direct `EditorConfigService` integrations using a custom `IWorkspaceManager` must migrate writes to the registered `WorkspaceManager`/`FileWatcherService` composition; the existing public constructor remains usable for reads, but cannot coordinate writes with an alternate manager. In-workspace writes refresh diagnostics and coordinate server versus external edit attribution; POSIX uses an advisory lock and byte checks, with the remaining uncooperative same-inode race tracked as `editorconfig-posix-uncooperative-write-race`. Closes `editorconfig-write-no-workspace-invalidation`, `editorconfig-write-outside-workspace`, and `filewatcher-markstaleifrelevant-stale-precedence-comment`.
