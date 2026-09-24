# move-type-to-project-unconditional-reference — Add the source→target ProjectReference only when the source still uses the moved type

**row:** `move-type-to-project-unconditional-reference` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/CrossProjectRefactoringService.cs:94`

## Acceptance

- [ ] Moving an unused type to SampleApp (which references SampleLib) succeeds
- [ ] Target file named after the type, not the source file (:74)

## Evidence

- move_type_to_project_preview(TrulyUnusedConcreteType → SampleApp) → InvalidOperation (cycle). — see `ai_docs/audits/20260924-1305/report.md` (check C1) and `ai_docs/audits/20260924-1305/findings.json`
