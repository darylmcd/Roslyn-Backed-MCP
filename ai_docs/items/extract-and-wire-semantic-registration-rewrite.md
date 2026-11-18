# extract-and-wire-semantic-registration-rewrite — Rewrite bound DI registrations semantically

**row:** `extract-and-wire-semantic-registration-rewrite` · **pri:** `Medium` · **size:** `M` · **deps:** `cross-project-interface-reference-binding`

## Anchors

- `src/RoslynMcp.Roslyn/Services/ExtractAndWireOrchestrator.cs:188`

## Acceptance

- [ ] Bind supported registration invocations and selected implementation symbols; edit syntax type arguments with the generated interface identity.
- [ ] Preserve comments/literals/unrelated same-name methods/types and whitespace; support qualified/spaced generic registrations with compilation controls.

## Evidence

- Source audit at main `fc5bb88676214a942daa727c7ca88ac8dd34b61c`. RewriteDiRegistration is a chain of ordinal String.Replace calls. The same method remains textual in the retained branch, so neither branch retention nor its namespace fix closes this finding.
- Source: closed PR #1806 head `d333c2e9`, kept by GitHub as `refs/pull/1806/head` (archive branch and tag deleted 2026-10-27). No build/test ran in this content audit; historical receipts are not current runtime qualification.

## Notes

2026-11-17 screen at c705e2e1. The experimental mutation can be fixed in 5.x after decomposing the shared defect. ExtractAndWireOrchestrator.cs:48–91,171–176 and SymbolRefactorService.cs:541–674 select by simple name and rewrite source text. Plan exact framework method and implementation binding for both, preserve existing registration shape and alias/trivia, and bound complete token review. The stable get_di_registrations inventory needs its existing separate compatibility screen.
