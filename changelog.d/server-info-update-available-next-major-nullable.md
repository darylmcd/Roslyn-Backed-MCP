---
category: Changed — BREAKING
---
- **Changed — BREAKING:** `server_info.update.updateAvailable` is now `boolean | null` (output schema `["boolean","null"]`). It is `true` when a known registry version is strictly newer (`latest` and `command` populated), `false` only when `update.checkStatus` is `succeeded` and no newer version exists, and `null` (unknown) while the check is `neverChecked`, `pending`, `failed` or `timedOut` with no newer version known; 4.x reported `false` in those states. Migration: treat `null` as unknown instead of "no update", read `update.checkStatus` for the reason, and widen any typed binding of `updateAvailable` to a nullable boolean.
