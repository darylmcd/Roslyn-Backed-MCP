# structured-content-tools-omit-meta — Include _meta timing in structuredContent for the 8 outputSchema tools

**row:** `structured-content-tools-omit-meta` · **pri:** `Low` · **size:** `M`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/ServerTools.cs:103`
- `src/RoslynMcp.Host.Stdio/Tools/WorkspaceTools.cs:383`

## Acceptance

- [ ] workspace_status/workspace_list/server_info structuredContent carry _meta (or outputSchema documents where it is)

## Evidence

- Registered client shows no _meta for workspace_list, workspace_status, server_info, server_heartbeat, workspace_health, workspace_drift_check (structuredContent channel, UseStructuredContent=true). — see `ai_docs/audits/20260924-1305/report.md` (check C4) and `ai_docs/audits/20260924-1305/findings.json`
