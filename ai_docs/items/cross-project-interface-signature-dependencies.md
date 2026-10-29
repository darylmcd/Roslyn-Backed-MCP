# cross-project-interface-signature-dependencies — Validate emitted interface signature dependencies

**row:** `cross-project-interface-signature-dependencies` · **pri:** `High` · **size:** `M` · **deps:** `cross-project-interface-reference-binding`

## Anchors

- `src/RoslynMcp.Roslyn/Services/CrossProjectRefactoringService.cs`
- `src/RoslynMcp.Roslyn/Helpers/ProjectGraphHelpers.cs`

## Acceptance

- [ ] Derive dependency closure from all emitted signature types including nested generic arguments, constraints, properties and events.
- [ ] Preserve symbol identity, aliases/reference metadata and valid acyclic dependencies; refuse concrete unsupported/cyclic closure before issuing a token.
- [ ] Cover direct extraction, dependency inversion and extract-and-wire through semantic compilation, including source-owned-signature cycles.

## Evidence

- Source audit at main `fc5bb88676214a942daa727c7ca88ac8dd34b61c`. Current extraction gathers namespaces and adds source-to-interface-project dependency; namespace imports alone do not give the target assembly the signature-type dependency. Retained item remains an unmerged finding.
- Source: closed PR #1806 head `d333c2e9`, kept by GitHub as `refs/pull/1806/head` (archive branch and tag deleted 2026-10-27). No build/test ran in this content audit; historical receipts are not current runtime qualification.
