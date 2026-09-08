# retire-cursor-roslyn-mcp-config — Remove the second repository-scoped MCP registration

**row:** `retire-cursor-roslyn-mcp-config` · **pri:** `Low` · **size:** `M`

## Anchors

- `.gitignore`
- `.cursor/mcp.json`

## Acceptance

- [ ] Delete the tracked Cursor MCP registration and remove the `.gitignore` exception that preserves it.
- [ ] Keep `.cursor/rules/` and the published plugin descriptor tracked and byte-stable.
- [ ] Add one repository-state matrix proving root and Cursor registrations are absent/ignored while Cursor rules and the public plugin descriptor remain tracked.

## Evidence

- `.cursor/mcp.json` remains tracked and `.gitignore:531-536` explicitly preserves it after the root registration was retired, leaving a second checkout-scoped startup contract.
