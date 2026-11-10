# symbol-resolver-find-document-project-blind — Make path-only document lookups project-aware

**row:** `symbol-resolver-find-document-project-blind` · **pri:** `Medium` · **size:** `L` · **verified:** `e220816e3fa161caca44362acb8c70e9390e73bf`

## Anchors

- `src/RoslynMcp.Roslyn/Helpers/SymbolResolver.cs:563` — FindDocument returns the first project's document for a path.
- `src/RoslynMcp.Roslyn/Services/FileOperationService.cs:35` — delete/move previews call FindDocument (also :108, :133, :141).
- `src/RoslynMcp.Roslyn/Services/TestDiscoveryService.cs:474` — FindDocumentByPath, first path match.
- `src/RoslynMcp.Roslyn/Services/FlowAnalysisService.cs:202` — inline first path match.
- `src/RoslynMcp.Roslyn/Services/SymbolRefactorService.cs:344` — FindDocument; also :1096 path match.
- `src/RoslynMcp.Roslyn/Services/TypeMoveService.cs:25` — FindDocument; also :89 path match.
- `tests/RoslynMcp.Tests/RebaseDocumentIdentityTests.cs`

## Acceptance

- [ ] Triage each site above for whether project identity matters; record the verdict per site.
- [ ] Fix every site where it matters, each with a red-first test using a linked file shared by two projects.
- [ ] Operator decision recorded: delete/move semantics for a linked file (first project only, all projects, or refuse as ambiguous).

## Evidence

- 2026-11-10 remediation (PRs #2055-#2057): PR #2055 fixed rebase document matching to the reviewed project; FileOperationService previews still target only the first project's document for a linked file. NamespaceRelocationService was checked and matches by type/namespace, not path.

## Notes

### Context

Size L: the six production files share one defect mechanism (path-only first-match document lookup) and one linked-file semantics decision, so triage and the shared helper land together; sites that do not need project identity close as triaged-no-change.
