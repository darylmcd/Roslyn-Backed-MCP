# discover-capabilities-unknown-category-matches-all — Reject or flag unknown taskCategory in discover_capabilities

**row:** `discover-capabilities-unknown-category-matches-all` · **pri:** `Low` · **size:** `M`

## Anchors

- `src/RoslynMcp.Host.Stdio/Prompts/PromptMessageBuilder.cs:87`
- `src/RoslynMcp.Host.Stdio/Prompts/RoslynPrompts.AnalysisWorkflows.cs:133`

## Acceptance

- [ ] taskCategory='nonsense' lists valid categories
- [ ] Rendered text no longer claims every tool needs a workspaceId

## Evidence

- discover_capabilities{taskCategory:'nonsense-category'} → 'capabilities relevant to nonsense-category: Tools (174)…'. — see `ai_docs/audits/20260924-1305/report.md` (check C3) and `ai_docs/audits/20260924-1305/findings.json`
