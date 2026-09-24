# find-implementations-corlib-guard-blocks-source-anchor — Let source-anchored find_implementations bypass the corlib-root guard

**row:** `find-implementations-corlib-guard-blocks-source-anchor` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/SymbolTools.cs:350`

## Acceptance

- [ ] find_implementations at a source position on an IDisposable token returns the workspace's implementers (26 by rg in W_RO)
- [ ] The metadataName path either returns them too or its hint points at a path that works
- [ ] Regression test pins both locator kinds

## Evidence

- find_implementations(WorkspaceManager.cs:22:59 IDisposable) → count 0 + hint 'Source-anchor the query instead'. Prior audit 2026-05-31 (find-implementations-corlib-metadataname-zero): source-anchored returned 17 — now 0. — see `ai_docs/audits/20260924-1305/report.md` (check C1) and `ai_docs/audits/20260924-1305/findings.json`
