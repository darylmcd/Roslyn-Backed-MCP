# retire-root-roslyn-mcp-registration — Remove the redundant root MCP registration

**row:** `retire-root-roslyn-mcp-registration` · **pri:** `Low` · **size:** `M` · **deps:** `user-scoped-roslyn-mcp-guidance`

## Anchors

- `.gitignore`
- `.mcp.json`
- `AGENTS.md`
- `ai_docs/architecture.md`

## Acceptance

- [ ] Delete the root Roslyn-only registration and its file-triggered bootstrap while retaining and documenting the distinct shipped plugin descriptor.

## Regression shape

Assert the root `.mcp.json` is absent, the AGENTS bootstrap trigger is absent, and fresh heartbeat, server-info, and catalog-parity probes succeed independently.

## Evidence

- The root registration duplicates the shipped plugin descriptor; implementation is prepared in PR #1483 and must follow the guidance child.
