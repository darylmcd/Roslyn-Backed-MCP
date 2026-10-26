# interface-extraction-nullability-context-fidelity — Preserve generated nullable contracts

**row:** `interface-extraction-nullability-context-fidelity` · **pri:** `High` · **size:** `M` · **deps:** `cross-project-interface-parameter-semantics,interface-extraction-ref-return-contracts,interface-extraction-unsafe-signature-context,interface-extraction-escaped-member-identifiers,interface-extraction-nullable-context-projection`

## Anchors

- `src/RoslynMcp.Roslyn/Services/InterfaceExtractionService.cs:246` (`BuildInterfaceFile`)
- `src/RoslynMcp.Roslyn/Services/CrossProjectRefactoringService.cs:538` (`CreateInterfaceCompilationUnit`)
- `tests/RoslynMcp.Tests/DetachedFixtureNativeContextIntegrationTests.cs`

## Acceptance

- [ ] Preserve semantic None, NotAnnotated and Annotated reference-type states across all currently emitted declaration type positions, including recursive generic, array, tuple and function-pointer types where supported.
- [ ] Preserve source-local nullable directives under different receiving defaults without changing project options or enabling all oblivious contracts.
- [ ] Prove valid-before source-local nullable-enabled members in nullable-disabled warnings-as-errors projects fail before repair; compile and compare actual serialized preview and applied/reloaded interface symbols and calls through public routes.
- [ ] Keep generic declaration features, cross-project event omission and assembly dependency closure in their separate owners.

## Evidence

- ACTIVE dependency checkpoint: PR1849 merged27f82e45 and PR1846 merged5b1930a3 with cold PASS and all required hosted checks green. This objective follows the active nullable-context child through its existing dependency chain; implementation remains separate from preserved combined draft PR1840. Original Acceptance and complex candidate tests remain unchanged. Native134/139 causal findings stay OPEN; ownership prevention does not prove their cause. See ai_docs/reports/2026-10-26-interface-split-delivery.md.


- Cold source review at c5511d5c: generated compilation units omit source nullable directives while semantic projection preserves nullable syntax. receiving-probe-final.log records valid-before source-local enable under project Disable, then emitted declaration CS8632; explicit context decoration compiles. Recursive generic/array/tuple/function-pointer contexts are compiler-proven representable; generic parent context must be restored at its closing token.
- PR1840 hosted run 40275116966 has nine CS8632 failures per Windows2/Linux2 shard despite known fixture XML Nullable=enable. Exact unchanged local private-temp 44-case replay passes; actual hosted options remain unobserved. This distinct discrepancy is under diagnostic-only investigation and is not attributed to the disabled-context defect.
- Seven-row batch holds PR1840 draft until this exposed defect is repaired and reviewed. Revision2 plan requires recursive None/NotAnnotated/Annotated preservation plus absolute receiving-context proof; project policy stays unchanged.

- Current delivery order, superseding fixture diagnosis and preserved-candidate receipt limits: `ai_docs/reports/2026-10-26-interface-split-delivery.md`. Historical pending/hold statements above describe the original combined candidate; use that report for current state.
