# project-filter-unknown-name-metrics-services — Reject unknown projectFilter in metrics and namespace-dependency services

**row:** `project-filter-unknown-name-metrics-services` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/CodeMetricsService.cs:47`
- `src/RoslynMcp.Roslyn/Services/CohesionAnalysisService.cs:74`
- `src/RoslynMcp.Roslyn/Services/CouplingAnalysisService.cs:75`
- `src/RoslynMcp.Roslyn/Services/NamespaceDependencyService.cs:33`
- `tests/RoslynMcp.Tests/ProjectFilterUnknownNameMetricsTests.cs` (new)

## Acceptance

- [ ] Each anchored call site resolves its project scope through `ProjectFilterHelper.ResolveProjects`, so a non-blank filter that matches no loaded project throws the canonical `projectName '<x>' matched 0 projects` ArgumentException instead of returning an empty, success-shaped result.
- [ ] Blank/null filters still scan the whole solution; Cohesion/Coupling keep their `excludeTestProjects` post-filter (applied after resolution, so a filter that names only a test project still resolves, then filters).
- [ ] One test per service: an unknown project name produces an InvalidArgument-class failure; a known name still returns results.

## Evidence

- HEAD 6f31f065 `CodeMetricsService.cs:47`: `var projects = ProjectFilterHelper.FilterProjects(solution, projectFilter);` then `documents = projects.SelectMany(p => p.Documents);` — an unknown name yields zero documents and a normal empty result.
- `CohesionAnalysisService.cs:74` and `CouplingAnalysisService.cs:75`: `ProjectFilterHelper.FilterProjects(solution, projectFilter)` followed by `.Where(p => !excludeTestProjects || ...)`.
- `NamespaceDependencyService.cs:33`: `var projects = ProjectFilterHelper.FilterProjects(solution, projectFilter);`.
- The throwing sibling exists: `ProjectFilterHelper.cs:24` `public static IReadOnlyList<Project> ResolveProjects(Solution solution, string? projectFilter)` (PR #1651, row `unknown-projectname-silently-empty`), adopted only by `AnalyzerInfoService` and `DiagnosticQueryService`.
- Split by subsystem from the ~13 remaining `FilterProjects` callers; siblings `project-filter-unknown-name-pattern-deadcode` and `project-filter-unknown-name-format-exception-di`. `CompileCheckService`, `FixAllTargetResolver` and `MsBuildEvaluationService` already handle the empty case and are out of scope.

Source: backlog-remediate 20260926T234932Z follow-up (Directive #3 spin-off of PR #1651).
