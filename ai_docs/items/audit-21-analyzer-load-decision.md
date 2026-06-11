# audit-21-analyzer-load-decision — decide execute-vs-park for the IDE/CA analyzer parity plan

**row:** `audit-21-analyzer-load-decision` · **pri:** `Medium` · **size:** `M` <!-- cache — the backlog row is canonical for pri/size; refresh on open if they disagree -->

## Anchors

- `ai_docs/plans/audit-21-analyzers/plan.md`
- `ai_docs/architecture.md:83`
- `docs/parity-gap-implementation-plan.md:76`
- `src/RoslynMcp.Roslyn/Services/CompilationCache.cs`

## Acceptance

- [ ] Decision recorded: (a) execute via `/backlog-sweep:prepare` against the plan (sweep-shaped, ~12K-token plan), OR (b) re-status the plan to `superseded`/parked with the explicit product trigger so a dormant Draft stops sitting in the active router
- [ ] Plan's §13 closure criterion fixed — it still cites the removed row `compile-check-vs-analyzers-doc`

## Evidence

- No host-analyzer ship commits in `git log --all`; `IHostAnalyzerProvider` absent from `src/`; distinct from brainstorm BRAIN-010 (parity transparency report).

## Context

AUDIT-21: IDE/CA analyzers are not host-injected into `MSBuildWorkspace`, so Roslyn-MCP diagnostics can differ from Visual Studio / Rider. Real unshipped gap with a detailed Draft plan (`ai_docs/plans/audit-21-analyzers/plan.md`, status active/dormant, owner TBD) but no prior backlog tracking; `docs/parity-gap-implementation-plan.md` gates it on "fix only if product requires full analyzer load".

Blocked-on: product decision (does the product require full IDE/CA analyzer parity?). Source: 2026-05-28 discovery-sweep work-search + doc-audit Known Gap.
