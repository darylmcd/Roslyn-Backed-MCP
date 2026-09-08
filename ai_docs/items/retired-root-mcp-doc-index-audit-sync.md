# retired-root-mcp-doc-index-audit-sync — Synchronize current documentation after root registration retirement

**row:** `retired-root-mcp-doc-index-audit-sync` · **pri:** `Low` · **size:** `M`

## Anchors

- `docs/README.md`
- `.ai-doc-audit.md`

## Acceptance

- [ ] Update the human-facing document index to identify the published plugin descriptor as shipped and external user/session configuration as the runtime registration.
- [ ] Refresh current `.ai-doc-audit.md` state through the doc-audit owner so it records the root config as absent/ignored and the AGENTS router as current.
- [ ] Preserve dated historical evidence and add one current-state inventory regression that rejects root-present or root-shipped claims outside explicitly dated history.

## Evidence

- `docs/README.md:40` still presents root `.mcp.json` as a shipped plugin source; `.ai-doc-audit.md:18,34,39` retains the same current-state claim and the superseded AGENTS bootstrap finding.
