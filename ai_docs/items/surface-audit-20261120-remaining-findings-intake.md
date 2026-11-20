# surface-audit-20261120-remaining-findings-intake — Intake the remaining 2026-11-20 surface-audit findings

**row:** `surface-audit-20261120-remaining-findings-intake` · **pri:** `Low` · **size:** `S` · **verified:** `d3ab02228685f3cc015691f32c0292c6e6526b43`

## Anchors

- `audit-reports/_latest-promotion-scorecard.json`

## Acceptance

- [ ] Run /backlog-intake over the local report on the machine that holds it; dedupe against the rows filed on 2026-11-20 and the existing refusal-message, token-reason and newline rows.
- [ ] Unfiled P2 items: G2-F4 (symbol_info strict mode at whitespace), G2-F5 (find_type_consumers silent truncation), TEARDOWN (worktree lock after workspace_close drain).

## Evidence

- Sections 13, 14 and 17 of the report.
- Finding id and full call evidence: audit-reports/20261120T171538Z_roslynmcp_mcp-server-surface-test.md (local, git-ignored).
