# find-references-closest-matches-rank-anonymous-members — Rank closestMatches by name similarity, not raw substring

**row:** `find-references-closest-matches-rank-anonymous-members` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Helpers/SymbolResolver.cs:493`

## Acceptance

- [ ] find_references(metadataName=RoslynMcp.Core.Services.IWorkspaceManager) suggests RoslynMcp.Roslyn.Contracts.IWorkspaceManager first
- [ ] Compiler-generated symbols excluded from suggestions

## Evidence

- closestMatches = <>f__AnonymousType0.i/.p/.r, <>f__AnonymousType1.m/.n; real type absent. query.Contains(simpleName) scores 1-char names; ordinal tiebreak (:254) sorts <>f__ first. 7.8 s cold. — see `ai_docs/audits/20260924-1305/report.md` (check C1) and `ai_docs/audits/20260924-1305/findings.json`
