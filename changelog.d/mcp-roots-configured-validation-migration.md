---
category: Changed — BREAKING
---

- **Changed — BREAKING:** File-path validation and solution discovery now use the server-owned `ROSLYNMCP_SANCTIONED_ROOTS` boundary instead of deprecated client `roots/list`; empty configuration fails closed unless `ROSLYNMCP_PATH_VALIDATION_FAIL_OPEN=true`, legacy client Roots can only narrow configured access, file-anchored discovery never enumerates above the configured boundary, and symlink/junction targets plus parent traversal are recursively resolved in physical order. Configure a `Path.PathSeparator`-delimited root list before upgrading. Sibling-worktree expansion now requires both `ROSLYNMCP_ALLOW_ROOT_EXPANSION=true` on the server and `expandSanctionedRoots=true` on the request; client input alone cannot widen access. Closes `mcp-roots-configured-validation-migration` and `mcp-roots-query-discovery-migration`.
