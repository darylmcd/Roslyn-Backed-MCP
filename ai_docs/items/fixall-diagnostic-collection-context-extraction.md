# fixall-diagnostic-collection-context-extraction — Extract FixAll diagnostic collection

**row:** `fixall-diagnostic-collection-context-extraction` · **pri:** `Low` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/FixAllService.cs`
- `tests/RoslynMcp.Tests/FixAllServiceTests.cs`

## Acceptance

- [ ] Replace `CollectDiagnosticsAsync`'s nine-parameter call with one immutable request context and an internal collector collaborator.
- [ ] Preserve analyzer-versus-compiler fallback, scope filtering, cancellation, and compilation-cache ownership.
- [ ] One table-driven regression compares document, project, and solution collection results before and after extraction.

## Evidence

The 2026-08-28 complexity review measured `CollectDiagnosticsAsync` at complexity 14, 67 lines, and nine parameters. The collector mixes scope traversal, compilation access, analyzer selection, and result grouping inside `FixAllService`.
