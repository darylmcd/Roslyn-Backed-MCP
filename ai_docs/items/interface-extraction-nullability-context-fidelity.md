# interface-extraction-nullability-context-fidelity — Preserve generated nullable contracts

**row:** `interface-extraction-nullability-context-fidelity` · **pri:** `High` · **size:** `M` · **deps:** `cross-project-interface-parameter-semantics,interface-extraction-ref-return-contracts,interface-extraction-unsafe-signature-context,interface-extraction-escaped-member-identifiers`

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

- PARKED with preserved drafts PR1840 and PR1854: dependency PR1854 atd8a70992 remains unqualified because Linux1 run40412863293 hit its30m deadline after2660passed/0failed/37skipped. The separate Windows owner-startup repair merged in PR1855 at49e0b03a1ed9d63c3f448a2bf545aeffc36c88bb; it does not establish a Linux cure. Integrate that prerequisite when this child resumes, before exact-head qualification. Live Docker/WSL provide7.75GiB; reviewed3GiB structural experiment stopped86 at memory cap before tests. No causally supported Linux correction or matching-capacity measurement is available in this session. Resume ci-linux-test-shard-budget with an adequate isolated Linux backend, corrected canonical Git remote and the reviewed fixed-source cohort; qualify the child before dependent code. Original Acceptance and full complex candidate tests stay unchanged; no weakened test or unchanged CI rerun.  See ai_docs/reports/2026-10-26-interface-split-delivery.md.


- Cold source review at c5511d5c: generated compilation units omit source nullable directives while semantic projection preserves nullable syntax. receiving-probe-final.log records valid-before source-local enable under project Disable, then emitted declaration CS8632; explicit context decoration compiles. Recursive generic/array/tuple/function-pointer contexts are compiler-proven representable; generic parent context must be restored at its closing token.
- PR1840 hosted run 40275116966 has nine CS8632 failures per Windows2/Linux2 shard despite known fixture XML Nullable=enable. Exact unchanged local private-temp 44-case replay passes; actual hosted options remain unobserved. This distinct discrepancy is under diagnostic-only investigation and is not attributed to the disabled-context defect.
- Seven-row batch holds PR1840 draft until this exposed defect is repaired and reviewed. Revision2 plan requires recursive None/NotAnnotated/Annotated preservation plus absolute receiving-context proof; project policy stays unchanged.

- Current delivery order, superseding fixture diagnosis and preserved-candidate receipt limits: `ai_docs/reports/2026-10-26-interface-split-delivery.md`. Historical pending/hold statements above describe the original combined candidate; use that report for current state.
- 2026-10-27: dependency `interface-extraction-nullable-context-projection` closed by PR1854 (hosted Linux budget fixed by the third shard, PR1857). PR1840 closed as superseded by this decomposition; its combined candidate source and tests remain reference-only on branch `codex/interface-parameter-semantics` at `cb90656f`. Re-cut this row from current main; do not resume PR1840.
- 2026-10-27: the only copy of `InterfaceReceivingContextValidator.cs` (receiving-policy compile diff, invalid nullable context check, recursive `NullableShape` comparison) with `InterfaceReceivingContextValidatorTests.cs` / `InterfaceReceivingContextIntegrationTests.cs` is on remote branch `codex/interface-parameter-semantics-local` at `64d638cd` (four commits past `cb90656f`). Reference only: re-derive against main `InterfaceNullableProjection` and integrate with its legality check rather than running both; its refusal truncates to five ids and reports a synthetic CS8632.

