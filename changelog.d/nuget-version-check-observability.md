---
category: Maintenance
---

- **Maintenance:** `NuGetVersionChecker` now records an observable check status (`NeverChecked`/`Pending`/`Succeeded`/`Failed`/`TimedOut` via `LastCheckStatus`/`LastCheckedAt`) and logs fetch failures at Debug instead of silently swallowing them, distinguishing a timed-out check from other failures. The non-blocking, never-throws contract of `GetLatestVersion()` is unchanged; `latest-version-status-surface` subsequently exposes the status through `server_info.update`. Closes `nuget-version-check-observability`.
