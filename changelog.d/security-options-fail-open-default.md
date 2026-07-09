---
category: Changed — BREAKING
---

- **Changed (BREAKING):** `SecurityOptions.PathValidationFailOpen` now defaults to `false` (fail-closed). Previously, an MCP client roots-lookup failure silently allowed file writes/edits through; it now rejects them unless `ROSLYNMCP_PATH_VALIDATION_FAIL_OPEN=true` is explicitly set. This closes a fail-open trust-boundary gap on path validation.
