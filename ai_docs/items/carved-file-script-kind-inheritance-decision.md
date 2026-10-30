# carved-file-script-kind-inheritance-decision — Decide Script-kind inheritance for carved files

**row:** `carved-file-script-kind-inheritance-decision` · **pri:** `Defer` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/TypeMoveService.cs:123`
- `src/RoslynMcp.Roslyn/Services/TypeExtractionService.cs:151`
- `src/RoslynMcp.Roslyn/Services/NamespaceRelocationService.cs:355`
- `src/RoslynMcp.Roslyn/Services/InterfaceExtractionService.cs:108`

## Acceptance

- [ ] Operator decision recorded (ADR or row note): inherit, force Regular, or refuse; then implement with tests.

## Evidence

- Product question surfaced by PR 1908 planning; MSBuildWorkspace never assigns Script to a .cs Compile item, so reachability is limited to synthetic or .csx sources.
- Sighted during the 2026-10-29 remediation of refactoring-rebase-source-kind-preservation, cross-project-interface-reference-binding and automatic-reload-analyzer-reader-lifetime (PRs 1908-1910). Not re-derived beyond the cold reviews.
