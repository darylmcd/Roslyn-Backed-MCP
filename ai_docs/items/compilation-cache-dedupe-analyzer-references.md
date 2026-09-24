# compilation-cache-dedupe-analyzer-references — Dedupe analyzer references by assembly identity in CompilationCache

**row:** `compilation-cache-dedupe-analyzer-references` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/CompilationCache.cs:247`

## Acceptance

- [ ] A project referencing the same analyzer assembly twice reports each diagnostic once
- [ ] Regression test

## Evidence

- CA1822 reported 64 times for 32 unique occurrences on the sample solution. — see `ai_docs/audits/20260924-1305/report.md` (check C1) and `ai_docs/audits/20260924-1305/findings.json`
