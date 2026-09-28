---
category: Changed
---

- **Changed:** `server_info.update.updateAvailable` keeps its 4.x boolean type, schema, and meaning (`true` only when a known registry version is strictly newer, otherwise `false`, including while the check is `neverChecked`, `pending`, `failed`, or `timedOut`); `update.checkStatus` with `lastCheckedAt` is now the documented authority for whether `false` means up to date (`succeeded`) or unknown, and `/roslyn-mcp:update` reads it instead of reporting "no update" from an unfinished check. **Deprecation:** 5.0 will make `updateAvailable` nullable (`null` = unknown); read `checkStatus` before trusting `false`. Closes `server-info-update-unknown-not-false`.
