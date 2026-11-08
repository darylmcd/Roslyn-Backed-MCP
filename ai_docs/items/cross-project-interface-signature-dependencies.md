# cross-project-interface-signature-dependencies — Validate emitted interface signature dependencies

**row:** `cross-project-interface-signature-dependencies` · **pri:** `High` · **size:** `M` · **deps:** `cross-project-interface-signature-compile-gate`

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

## Notes

Cold plan review 2026-11-05: (F8) new refusals require an ADR and Changed - BREAKING changelog category; (F9) source-owned IAnimal and the tolerant AssertNoUnresolvedNamesAsync affect at least 15 tests across four files; (F10) unconditional unused alias and using-static copies in UsingDirectiveSynthesizer.cs:108-112 can create new compiler errors, so the compile gate must compare baseline with pending diagnostics and test these cases; (F11) resolve metadata owners by symbol identity, never simple assembly name.

Cold re-review 2026-11-07: harness-first and an unused walker-first split were rejected. Delivery order is valid acyclic reference closure (`cross-project-interface-signature-reference-closure`), cyclic-refusal behavior (`cross-project-interface-signature-refusal-compile`), then compilation gate and strict test migration (`cross-project-interface-signature-compile-gate`). Keep source-owned fixture semantics until refusal ships. The parent closes only after all three slices merge and meet its acceptance.
