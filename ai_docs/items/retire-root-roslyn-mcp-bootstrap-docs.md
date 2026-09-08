# retire-root-roslyn-mcp-bootstrap-docs — Remove obsolete root-registration documentation

**row:** `retire-root-roslyn-mcp-bootstrap-docs` · **pri:** `Low` · **size:** `M` · **deps:** `user-scoped-roslyn-mcp-editor-guidance,user-scoped-roslyn-mcp-runtime-guidance`

## Anchors

- `AGENTS.md`
- `ai_docs/architecture.md`

## Acceptance

- [ ] Remove the file-triggered root MCP bootstrap and distinguish the shipped plugin descriptor from external user/session registration.

## Regression shape

Assert the bootstrap trigger is absent and the architecture inventory separately names the shipped plugin descriptor and external registration.

## Evidence

- PR #1483 contains the prepared changes for both anchors and records fresh heartbeat, server-info, and catalog-parity evidence.

## Context

Split from `retire-root-roslyn-mcp-registration`, sourced from global parent `bl-0207`; both guidance children must land first, and closing this row also deletes this detail file.
