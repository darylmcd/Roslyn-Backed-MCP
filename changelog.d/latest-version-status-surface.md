---
category: Changed
---

- **Changed:** `server_info.update` now carries latest-version check status (`checkStatus`) and completion time (`lastCheckedAt`) so operators can distinguish pending, failed, timed-out, and succeeded-with-no-update states even when `latest` is `null`. Closes `latest-version-status-surface`.
