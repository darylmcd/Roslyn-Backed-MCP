# project-filter-helper-whitespace-normalize — normalize whitespace-only projectFilter inside ProjectFilterHelper so all 18 tool call sites share one gate

**row:** `project-filter-helper-whitespace-normalize` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Helpers/ProjectFilterHelper.cs:9` (`FilterProjects` gates on `projectFilter is null`)
- `src/RoslynMcp.Roslyn/Services/FormatVerifyService.cs:31`
- `src/RoslynMcp.Roslyn/Services/AnalyzerInfoService.cs:30`
- `src/RoslynMcp.Roslyn/Services/CompileCheckService.cs:42` (the caller-side normalization PR #1163 added, redundant once fixed here)

## Acceptance

- [ ] `ProjectFilterHelper.FilterProjects` treats a whitespace-only `projectFilter` as "no filter" (returns all solution projects), matching the semantics `compile_check` now enforces caller-side; the redundant normalization line in `CompileCheckService.CheckAsync` is removed.
- [ ] A regression test covers at least one non-`compile_check` consumer (e.g. `FormatVerifyService.CheckAsync` or `AnalyzerInfoService.ListAnalyzersAsync`) with `projectFilter: " "`, asserting it evaluates >0 projects instead of returning a silently-empty result.

## Evidence

- Code-quality review of PR #1163 (`compile-check-project-filter-normalize`): `FilterProjects` gates on `projectFilter is null`, so `" "` falls into a `string.Equals(p.Name, " ", OrdinalIgnoreCase)` that cannot match any real project name. Traced two live consumers: `FormatVerifyService.cs:31` iterates zero projects and returns `checkedCount=0` with no violations (a false green), and `AnalyzerInfoService.cs:30` returns an empty analyzer list. PR #1163 fixed only `compile_check`'s caller; 17 other `FilterProjects` call sites remain unfixed.

## Context

Spin-off from the `compile-check-project-filter-normalize` initiative (backlog-sweep plan `20260806T023001Z_backlog-sweep`, PR #1163), which the prepare-phase review already flagged as a compile_check-local containment rather than a root-cause fix (warn finding, rule 5b).
