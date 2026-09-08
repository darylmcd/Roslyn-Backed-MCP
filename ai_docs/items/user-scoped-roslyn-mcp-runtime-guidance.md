# user-scoped-roslyn-mcp-runtime-guidance — Refresh runtime MCP guidance

**row:** `user-scoped-roslyn-mcp-runtime-guidance` · **pri:** `Low` · **size:** `M`

## Anchors

- `ai_docs/README.md`
- `ai_docs/runtime.md`

## Acceptance

- [ ] Both runtime surfaces distinguish user/session-scoped configuration intent from verified liveness and stop relying on the repository-local transition file.

## Regression shape

Inventory both anchors, then report configured, documented, and verified-live `server_heartbeat` or `server_info` evidence separately.

## Evidence

- PR #1482 contains the prepared changes for both anchors and records fresh live-probe evidence.

## Context

Split from `user-scoped-roslyn-mcp-guidance`, sourced from global parent `bl-0207`; closing this row also deletes this detail file.
