# formatter-baseline-six-stale-imports — Remove stale import allowances

**row:** `formatter-baseline-six-stale-imports` · **pri:** `Medium` · **size:** `M`

## Anchors

- `eng/format-baseline.json`
- `eng/generate-format-baseline.ps1`
- `tests/RoslynMcp.Tests/FormatterBaselineContractTests.cs`

## Acceptance

- [ ] Remove exactly the six stale IMPORTS allowances through the generator; do not alter unrelated live allowances or source files.
- [ ] On the resulting clean head, generator `-Check` exits 0 and the formatter baseline contract test passes.
- [ ] Record before/after inventory counts and prove the untouched base was already red.

## Evidence

- On untouched `origin/main` 37ba3daa, an admitted generator `-Check` exited 1 with `Formatter baseline artifact is stale`: live inventory 87 findings/83 files/76 IMPORTS; tracked artifact 93/89/82. The stale entries are IWorkspaceManager, ProjectMetadataParser, SolutionDiffHelper, NamespaceRelocationService, NamespaceRelocationTests, and ParameterObjectPreviewTests.
- Discovered while remediating `interface-extractor-import-order` and `msbuild-evaluation-service-import-order`; their three intended import repairs must land with an exact, current baseline for the required formatter contract.
