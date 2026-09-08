# user-scoped-roslyn-mcp-guidance — Refresh user-scoped MCP guidance

**row:** `user-scoped-roslyn-mcp-guidance` · **pri:** `Low` · **size:** `M`

## Anchors

- `.cursor/rules/operational-essentials.md`
- `.github/copilot-instructions.md`
- `ai_docs/README.md`
- `ai_docs/runtime.md`

## Acceptance

- [ ] All four active guidance surfaces identify user/session-scoped configuration as runtime intent and require live probes without claiming a repository-local declaration proves availability.

## Regression shape

Inventory the four guidance surfaces, then verify `server_heartbeat` and `server_info` are reported separately as documented, configured, and verified-live evidence.

## Evidence

- The `bl-0207` removal requires consumer guidance to stop depending on the redundant root registration first; implementation is prepared in PR #1482.
