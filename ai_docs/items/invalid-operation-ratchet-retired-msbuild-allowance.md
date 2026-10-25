# invalid-operation-ratchet-retired-msbuild-allowance - Retire the obsolete MSBuild constructor allowance

**row:** `invalid-operation-ratchet-retired-msbuild-allowance` · **pri:** `Low` · **size:** `S` · **deps:** `project-metadata-parser-broad-exception-fallbacks`

## Anchors

- `tests/RoslynMcp.Tests/TestData/invalid-operation-construction-baseline.json:285` - retired two-argument EvaluatePropertiesCore constructor fingerprint.
- `tests/RoslynMcp.Tests/InvalidOperationConstructionRatchetTests.cs:388` - ScanSites inventories constructor syntax.
- `tests/RoslynMcp.Tests/InvalidOperationConstructionRatchetTests.cs:981` - subset assertion permits retired baseline identities.
- `src/RoslynMcp.Roslyn/Helpers/MsBuildMetadataHelper.cs:51` - current helper takes global properties and uses InvalidOperationErrors.Internal (wrapper at line 63 after the classification fix).

## Acceptance

- [ ] Re-derive the actual constructor inventory and prove the obsolete EvaluatePropertiesCore identity is absent.
- [ ] Remove only its retired allowance; add no replacement allowance or suppression.
- [ ] Run the existing ratchet contracts and verify their inventory/allowance checks still pass. Preserve all unrelated baseline entries and factory behavior.

## Evidence

- Metadata failure-classification implementation inspection at base `7688a4e51ed678117cc3d0c4ee01cf2668125df9` found the historical signature `EvaluatePropertiesCore(System.String,System.String[])`, while the current method accepts IReadOnlyDictionary global properties and calls the Internal factory instead of constructing InvalidOperationException.
- ScanSites visits BaseObjectCreationExpressionSyntax, so it does not inventory the factory invocation; AssertSubset allows deleted inventory entries. This is a pre-existing stale allowance, separate from the parser catch classification mechanism; no ratchet failure is claimed.
