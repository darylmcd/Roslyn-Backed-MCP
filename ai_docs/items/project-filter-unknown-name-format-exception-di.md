# project-filter-unknown-name-format-exception-di — Reject unknown projectFilter in format-verify, exception-flow and DI-registration scans

**row:** `project-filter-unknown-name-format-exception-di` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/FormatVerifyService.cs:31`
- `src/RoslynMcp.Roslyn/Services/ExceptionFlowService.cs:71`
- `src/RoslynMcp.Roslyn/Services/DiRegistrationService.cs:221`
- `tests/RoslynMcp.Tests/ProjectFilterUnknownNameScanTests.cs` (new)

## Acceptance

- [ ] Each anchored call site resolves through `ProjectFilterHelper.ResolveProjects`; an unknown non-blank project name throws the canonical `matched 0 projects` ArgumentException instead of reporting zero violations / catch sites / registrations as success.
- [ ] `format_check` in particular no longer reports a clean result for a mistyped project name.
- [ ] Blank/null filter behavior is unchanged; a test per service covers the unknown-name case.

## Evidence

- HEAD 6f31f065 `FormatVerifyService.cs:31`: `var projects = ProjectFilterHelper.FilterProjects(solution, projectName);` — zero projects gives `checkedCount = 0` and no violations, i.e. a false clean.
- `ExceptionFlowService.cs:71`: `var projects = ProjectFilterHelper.FilterProjects(solution, scopeProjectFilter).ToList();`.
- `DiRegistrationService.cs:221`: `var projects = ProjectFilterHelper.FilterProjects(solution, projectFilter);`.
- Throwing sibling `ProjectFilterHelper.ResolveProjects` landed in PR #1651.
- Siblings: `project-filter-unknown-name-metrics-services`, `project-filter-unknown-name-pattern-deadcode`.

Source: backlog-remediate 20260926T234932Z follow-up (Directive #3 spin-off of PR #1651).
