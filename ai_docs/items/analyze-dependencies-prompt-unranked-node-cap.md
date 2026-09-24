# analyze-dependencies-prompt-unranked-node-cap — Rank analyze_dependencies prompt nodes before capping

**row:** `analyze-dependencies-prompt-unranked-node-cap` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Host.Stdio/Prompts/RoslynPrompts.cs:247`

## Acceptance

- [ ] Rendered prompt includes every namespace that appears in the cycles list
- [ ] External namespaces with typeCount 0 rank last

## Evidence

- Rendered nodes = 50 external namespaces (typeCount 0); RoslynMcp.Host.Stdio.Tools appears in cycles but not nodes. — see `ai_docs/audits/20260924-1305/report.md` (check C3) and `ai_docs/audits/20260924-1305/findings.json`
