# move-type-to-project-signature-dependency-edge — Add dependency edges for moved type bodies

**row:** `move-type-to-project-signature-dependency-edge` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/CrossProjectRefactoringService.cs:114`

## Acceptance

- [ ] Dependencies referenced by the moved type are given a valid, acyclic project reference or refused before a token issues; compile-asserted.

## Evidence

- Same one-edge blind spot as cross-project-interface-signature-dependencies but a different producer and mechanism (type bodies, not emitted signatures).
- Sighted during the 2026-10-29 remediation of refactoring-rebase-source-kind-preservation, cross-project-interface-reference-binding and automatic-reload-analyzer-reader-lifetime (PRs 1908-1910). Not re-derived beyond the cold reviews.
