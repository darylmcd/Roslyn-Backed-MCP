# project-filter-unknown-name-pattern-deadcode — Reject unknown projectFilter in pattern, duplicate and unused-code scans

**row:** `project-filter-unknown-name-pattern-deadcode` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/CodePatternAnalyzer.cs:63`
- `src/RoslynMcp.Roslyn/Services/DuplicateMethodDetectorService.cs:59`
- `src/RoslynMcp.Roslyn/Services/UnusedCodeAnalyzer.cs:88`
- `tests/RoslynMcp.Tests/ProjectFilterUnknownNamePatternTests.cs` (new)

## Acceptance

- [ ] Every `FilterProjects` call in the anchored files (CodePatternAnalyzer :63 and :195; UnusedCodeAnalyzer :88, :658, :704, :1254; DuplicateMethodDetectorService :59) resolves through `ProjectFilterHelper.ResolveProjects`, so an unknown non-blank filter throws the canonical `matched 0 projects` ArgumentException rather than returning an empty success.
- [ ] Blank/null filter behavior is unchanged.
- [ ] Tests cover reflection-usage / semantic-search, duplicate-method and unused-symbol entry points with an unknown project name.

## Evidence

- HEAD 6f31f065 `CodePatternAnalyzer.cs:63` and `:195`: `var projects = ProjectFilterHelper.FilterProjects(solution, projectFilter);`.
- `DuplicateMethodDetectorService.cs:59`: `var projects = ProjectFilterHelper.FilterProjects(solution, options.ProjectFilter);`.
- `UnusedCodeAnalyzer.cs:88`, `:658`, `:704`, `:1254`: `var projects = ProjectFilterHelper.FilterProjects(solution, options.ProjectFilter);` — each loop simply iterates zero projects.
- Throwing sibling `ProjectFilterHelper.ResolveProjects` landed in PR #1651.
- Siblings: `project-filter-unknown-name-metrics-services`, `project-filter-unknown-name-format-exception-di`.

Source: backlog-remediate 20260926T234932Z follow-up (Directive #3 spin-off of PR #1651).
