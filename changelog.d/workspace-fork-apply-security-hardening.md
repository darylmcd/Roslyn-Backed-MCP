---
category: Fixed
---

- **Fixed:** `workspace_fork_apply` no longer copies secret-bearing files (`.env`, `appsettings.*.json` except base/Development, `*.pubxml`, `secrets.json`, `*.pfx`, `*.key`) into forks; `retention=keep` forks now expire via a configurable TTL (`ROSLYNMCP_FORK_TTL_HOURS`, default 24h); the fork restore path and timeout are configurable via `ROSLYNMCP_FORK_DOTNET_PATH` / `ROSLYNMCP_FORK_RESTORE_TIMEOUT_MINUTES`; and `test_coverage` now deletes its temp coverage directory after aggregation.
