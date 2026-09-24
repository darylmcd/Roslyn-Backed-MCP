# unknown-tool-arguments-silently-ignored — Reject or warn on unknown tool arguments

**row:** `unknown-tool-arguments-silently-ignored` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Host.Stdio/Middleware/StructuredCallToolFilter.cs`

## Acceptance

- [ ] compile_check{workspaceId, severty:'Error'} returns InvalidArgument naming 'severty' and suggesting 'severity' (or succeeds with an explicit unknownArguments warning)
- [ ] inputSchema advertises additionalProperties:false if rejection is chosen
- [ ] Regression test covers one read and one write tool

## Evidence

- 175/175 inputSchemas lack additionalProperties:false; compile_check{…,severty:'Error'} and workspace_status{…,__bogus:1} succeed; the filter is silently dropped. — see `ai_docs/audits/20260924-1305/report.md` (check C4) and `ai_docs/audits/20260924-1305/findings.json`

## Context

- Pick one enforcement point in the call-tool pipeline (the filter that already builds schemaHint). Anchor file is the likely home; confirm during deepening.
