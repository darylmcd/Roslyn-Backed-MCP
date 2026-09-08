# user-scoped-roslyn-mcp-editor-guidance — Refresh editor-agent MCP guidance

**row:** `user-scoped-roslyn-mcp-editor-guidance` · **pri:** `Low` · **size:** `M`

## Anchors

- `.cursor/rules/operational-essentials.md`
- `.github/copilot-instructions.md`

## Acceptance

- [ ] Both editor-agent surfaces identify user/session-scoped configuration as intent and require a live probe without depending on a repository-local registration.

## Regression shape

Inventory both anchors, then report configured, documented, and verified-live `server_heartbeat` or `server_info` evidence separately.

## Evidence

- PR #1482 contains the prepared changes for both anchors and records fresh live-probe evidence.

## Context

Split from `user-scoped-roslyn-mcp-guidance`, sourced from global parent `bl-0207`; closing this row also deletes this detail file.
