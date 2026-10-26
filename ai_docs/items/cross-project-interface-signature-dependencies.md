# cross-project-interface-signature-dependencies — Validate emitted interface signature dependencies

**row:** `cross-project-interface-signature-dependencies` · **pri:** `High` · **size:** `M` · **deps:** —

## Anchors

- `src/RoslynMcp.Roslyn/Services/CrossProjectRefactoringService.cs`
- `src/RoslynMcp.Roslyn/Helpers/ProjectGraphHelpers.cs`

## Acceptance

- [ ] Derive dependency closure from all emitted signature types including nested generic arguments, constraints, properties and events.
- [ ] Preserve symbol identity, aliases/reference metadata and valid acyclic dependencies; refuse concrete unsupported/cyclic closure before issuing a token.
- [ ] Cover direct extraction, dependency inversion and extract-and-wire through semantic compilation, including source-owned-signature cycles.

## Evidence

- Source audit at main `fc5bb88676214a942daa727c7ca88ac8dd34b61c`. Current extraction gathers namespaces and adds source-to-interface-project dependency; namespace imports alone do not give the target assembly the signature-type dependency. Retained item remains an unmerged finding.
- Retained immutable source: `d333c2e96a1e23a31286cda53b920dbee5d6b7be`; preserved tag: `archive/pr1806-restore-reviewed-graph-20261025`. No build/test ran in this content audit; historical receipts are not current runtime qualification.
