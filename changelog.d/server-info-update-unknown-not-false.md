---
category: Changed — BREAKING
---

- **Changed — BREAKING:** Migration: treat `server_info.update.updateAvailable: null` as unknown and inspect `checkStatus`; `false` now means a successful check found no newer release.
