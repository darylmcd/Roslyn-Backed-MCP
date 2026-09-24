# workspace-close-schema-leaks-test-seams — Remove test seams from the workspace_close MCP schema

**row:** `workspace-close-schema-leaks-test-seams` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/WorkspaceTools.cs:143`

## Acceptance

- [ ] tools/list workspace_close inputSchema has only workspaceId and drainProcesses
- [ ] WorkspaceCloseDrainTests still inject a fake enumerator and short timeout via an internal overload
- [ ] A schema test asserts no tool exposes a param with $comment 'Unsupported .NET type' or without a description

## Evidence

- tools/list: getProcessesByName {"$comment":"Unsupported .NET type","not":true}, processDrainTimeout {TimeSpan pattern}, both undocumented; comment at WorkspaceTools.cs:141 claims 'no [Description], so the schema omits it' — false. Client-settable drain timeout. — see `ai_docs/audits/20260924-1305/report.md` (check C4) and `ai_docs/audits/20260924-1305/findings.json`

## Context

- The MCP SDK includes every non-DI parameter regardless of [Description]. Only workspace_close is affected (full-surface scan of 175 schemas).
