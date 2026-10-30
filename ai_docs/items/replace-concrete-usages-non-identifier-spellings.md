# replace-concrete-usages-non-identifier-spellings — Rewrite qualified and aliased concrete-type spellings

**row:** `replace-concrete-usages-non-identifier-spellings` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/InterfaceExtractionService.cs (ReplaceConcreteUsagesAsync)`

## Acceptance

- [ ] Qualified, aliased, return-type, local and generic-argument spellings are rewritten or explicitly refused; compile-asserted through the public producer.

## Evidence

- PR 1910 states the limitation in a code comment and PR body without fixing it; this row is the tracked remainder.
- Sighted during the 2026-10-29 remediation of refactoring-rebase-source-kind-preservation, cross-project-interface-reference-binding and automatic-reload-analyzer-reader-lifetime (PRs 1908-1910). Not re-derived beyond the cold reviews.
