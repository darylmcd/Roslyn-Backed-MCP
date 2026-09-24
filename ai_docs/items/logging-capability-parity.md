# logging-capability-parity — Align the logging capability between initialize and server_info

**row:** `logging-capability-parity` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/ServerTools.cs:150`

## Acceptance

- [ ] initialize.capabilities.logging and server_info.capabilities.logging agree
- [ ] If advertised, logging/setLevel changes what is emitted

## Evidence

- initialize → capabilities.logging {}; logging/setLevel → {} success; server_info capabilities.logging=false; no notifications/message observed. — see `ai_docs/audits/20260924-1305/report.md` (check C6) and `ai_docs/audits/20260924-1305/findings.json`
