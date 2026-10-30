# receiving-namespace-keyword-segment-escaping — Escape keyword segments in derived receiving namespaces

**row:** `receiving-namespace-keyword-segment-escaping` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/CrossProjectRefactoringService.cs` (DeriveTargetNamespace, DeriveTargetNamespaceForPath)
- `src/RoslynMcp.Roslyn/Helpers/ReceivingNamespaceFactory.cs`

## Acceptance

- [ ] A project RootNamespace such as Acme.namespace yields compilable generated declarations; red-first test through the cross-project producers.

## Evidence

- PR 1910 covers the escaped RootNamespace Acme.@namespace spelling only; an unescaped keyword segment is a namespace-emission defect, not a binding defect.
- Sighted during the 2026-10-29 remediation of refactoring-rebase-source-kind-preservation, cross-project-interface-reference-binding and automatic-reload-analyzer-reader-lifetime (PRs 1908-1910). Not re-derived beyond the cold reviews.
