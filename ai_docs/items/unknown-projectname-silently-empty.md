# unknown-projectname-silently-empty — Reject unknown projectName in project_diagnostics and list_analyzers

**row:** `unknown-projectname-silently-empty` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Helpers/ProjectFilterHelper.cs:7`

## Acceptance

- [ ] project_diagnostics(projectName=NoSuchProject) → InvalidArgument naming the loaded projects
- [ ] list_analyzers same
- [ ] Regression test

## Evidence

- project_diagnostics(projectName=NoSuchProject, summary) → all zeros; compile_check same input → InvalidArgument. — see `ai_docs/audits/20260924-1305/report.md` (check C1) and `ai_docs/audits/20260924-1305/findings.json`
