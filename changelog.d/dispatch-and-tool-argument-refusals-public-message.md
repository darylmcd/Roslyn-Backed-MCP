---
category: Changed — BREAKING
---
- **Changed — BREAKING:** Dispatch and tool argument refusals now return safe actionable guidance; ambiguous discovery no longer exposes candidate paths. This major-release security correction omits the normal deprecation window to remove sensitive detail immediately. Migration: call `workspace_load` with an operator-supplied explicit solution or project path, retry with its `workspaceId`, and use `workspace_status` for project names; use category and schema hints instead of parsing diagnostic prose. See ADR 0015 (`docs/decisions/0015-dispatch-public-argument-diagnostics.md`).
