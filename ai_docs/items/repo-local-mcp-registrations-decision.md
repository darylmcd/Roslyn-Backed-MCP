# repo-local-mcp-registrations-decision — Decide the tracked repo-local MCP registrations

**row:** `repo-local-mcp-registrations-decision` · **pri:** `Low` · **size:** `M`

# repo-local-mcp-registrations-decision — Decide the tracked repo-local MCP registrations

## Anchors

- `.cursor/mcp.json`
- `.vscode/mcp.json`
- `.gitignore`
- `.github/copilot-instructions.md`
- `.cursor/rules/operational-essentials.md`

## Acceptance

- [ ] Tracked registrations and the guidance agree.
- [ ] Any kept registration sets the sanctioned-roots env.

## Evidence

- .gitignore:531-536 tracks them deliberately; guidance in copilot-instructions.md and operational-essentials.md contradicts. (doc-audit 2026-09-23)
