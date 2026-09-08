# retire-root-roslyn-mcp-config — Remove the redundant root MCP configuration

**row:** `retire-root-roslyn-mcp-config` · **pri:** `Low` · **size:** `M` · **deps:** `user-scoped-roslyn-mcp-editor-guidance,user-scoped-roslyn-mcp-runtime-guidance`

## Anchors

- `.gitignore`
- `.mcp.json`

## Acceptance

- [ ] Delete the redundant root Roslyn-only registration and retain the ignore rule that keeps local project registrations untracked.

## Regression shape

Assert root `.mcp.json` is absent and `.gitignore` no longer force-includes it, then confirm the shipped `.claude-plugin/mcp.json` descriptor remains tracked.

## Evidence

- PR #1483 contains the prepared changes for both anchors and preserves the shipped plugin descriptor.

## Context

Split from `retire-root-roslyn-mcp-registration`, sourced from global parent `bl-0207`; both guidance children must land first, and closing this row also deletes this detail file.
