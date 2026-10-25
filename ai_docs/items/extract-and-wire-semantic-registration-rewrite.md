# extract-and-wire-semantic-registration-rewrite — Rewrite bound DI registrations semantically

**row:** `extract-and-wire-semantic-registration-rewrite` · **pri:** `Medium` · **size:** `M` · **deps:** —

## Anchors

- `src/RoslynMcp.Roslyn/Services/ExtractAndWireOrchestrator.cs:188`

## Acceptance

- [ ] Bind supported registration invocations and selected implementation symbols; edit syntax type arguments with the generated interface identity.
- [ ] Preserve comments/literals/unrelated same-name methods/types and whitespace; support qualified/spaced generic registrations with compilation controls.

## Evidence

- Source audit at main `fc5bb88676214a942daa727c7ca88ac8dd34b61c`. RewriteDiRegistration is a chain of ordinal String.Replace calls. The same method remains textual in the retained branch, so neither branch retention nor its namespace fix closes this finding.
- Retained immutable source: `d333c2e96a1e23a31286cda53b920dbee5d6b7be`; preserved tag: `archive/pr1806-restore-reviewed-graph-20261025`. No build/test ran in this content audit; historical receipts are not current runtime qualification.
