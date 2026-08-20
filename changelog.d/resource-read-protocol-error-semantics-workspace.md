---
category: Changed — BREAKING
---

- **Changed — BREAKING:** `resources/read` failures on workspace resources (`roslyn://workspaces*`, `roslyn://workspace/**`) now travel over the JSON-RPC error channel instead of being serialized into a successful `contents` body. Missing workspaces/files return `-32002` (`ResourceNotFound`) under protocol `2025-11-25` and `-32602` (`InvalidParams`) under `2026-07-28`; malformed line ranges return `-32602` in both eras; unexpected handler failures return a sanitized `-32603`. Error payloads carry only a stable category, remediation text, the requested resource URI, and a correlation id — never exception types, stack traces, or raw paths. **Migration:** clients that parsed `{"error":true,"category":...}` out of the resource body must read the JSON-RPC `error` member instead. Server-catalog resources migrate in the same release — see the companion entry for `roslyn://server/catalog*`, so the whole `resources/read` surface changes together.
