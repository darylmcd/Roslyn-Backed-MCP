---
category: Changed — BREAKING
---

- **Changed — BREAKING:** Retired MCP protocol logging and its stale capability, moved request correlation to an explicit incoming-message lifecycle, added an opt-in secret-safe structured stderr sink, removed false resource-list notifications from static workspace lifecycle operations, and stopped exposing unexpected exception types or stacks in tool results. Clients must stop using `logging/setLevel` or `notifications/message`, correlate safe internal-error references with operator stderr when enabled, and refresh resources only after an actual list-change notification. Closes `request-correlation-context-lifecycle`, `server-structured-observability-sink`, `mcp-logging-stderr-otel-migration`, and `workspace-resource-list-notification-semantics`; expected validation/not-found detail redaction remains tracked by `tool-error-envelope-sensitive-detail-disclosure`.
