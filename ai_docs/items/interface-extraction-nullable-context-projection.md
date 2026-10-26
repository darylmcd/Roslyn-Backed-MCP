# interface-extraction-nullable-context-projection — Preserve emitted nullable annotation contexts

**row:** `interface-extraction-nullable-context-projection` · **pri:** `High` · **size:** `M` · **deps:** `cross-project-receiving-namespace-compatibility,analyzer-shadow-owner-startup-sweep-race`

## Anchors

- `src/RoslynMcp.Roslyn/Helpers/InterfaceNullableProjection.cs` (new; preserved candidate is a reference, not current production)
- `src/RoslynMcp.Roslyn/Services/InterfaceExtractionService.cs` (generated interface document before preview storage)
- `src/RoslynMcp.Roslyn/Services/CrossProjectRefactoringService.cs` (actual emitted declaration/symbol pairs and all interface producers)
- `tests/RoslynMcp.Tests/InterfaceNullableContextProjectionIntegrationTests.cs` (new native public-route matrix)
- `tests/RoslynMcp.Tests/InterfaceNullableProjectionTests.cs` (new recursive helper and legality controls)

## Acceptance

- [ ] Preserve recursive None/NotAnnotated/Annotated states for every actually emitted type position through all four extraction routes. Pair actual non-null emitted declarations/positions with source-symbol roles; dropped fields/accessors/cross-project events must not corrupt mapping, and no emitted nullable slot may be silently omitted.
- [ ] Preserve source-local directives under different actual receiving defaults, including generated-file/editorconfig classification. Keep ordinary spelling and bytes unchanged when contexts match; never change project options or enable all oblivious contracts.
- [ ] Prove valid-before native public specimens for original-emitter methods/properties and standalone events under Enable/Disable receiving policy; old no-decoration output must fail graph/compilation oracles. Compare recursive symbols after actual serialized preview and apply/reload, runtime calls where applicable, untouched pre-apply disk bytes and unchanged policy, with strict loaded options and no native load errors.
- [ ] Cover complete helper recursion using proper raw source/interface syntax: indexer keys, ref/ref-readonly wrappers, pointer/function-pointer positions, generic containing qualifiers/arguments, arrays/ranks, tuples and Nullable<T>/nested wrappers. Preserve all candidate helper regression assertions and serialization-based compiler/graph checks; do not rely on new member projection to make helper tests meaningful.
- [ ] Ship compiler-backed legality of NEW nullable decoration atomically: actual receiving parse options/tree context, serialized directives and represented annotation states. Demonstrate C#7.3 oblivious plain-string compatibility and compiler-proven CS8370 refusal for nonoblivious decoration before any ordinary/composite store; preserve solution/version, source bytes/options and authored bounded diagnostic guidance. Do not substitute the full receiving validator or broad source-contract refusal.
- [ ] Preserve every existing parent Acceptance and full complex public nullable test unchanged for later correctly emitted indexer/function-pointer/ref contracts. This child closes only annotation-context projection; original emitter defects remain explicitly owned by dependent parents, without skipped/weakened tests, product input fences or falsely closing parent nullable/language rows.

## Evidence

- BLOCKED hosted qualification: draft PR1854 atd8a709921b5aa4a6841f6ee093fe28d777f4c1d5 has fresh implementation coldPASS, local449/0/0 plus final native28/0/0 and causal old25fail/3compatiblepass. Windows4 run40412863293 job121887797301 failed analyzer owner-file initialization; distinct Linux1 job121887797650 passed2660tests/0failed/37skipped in28m23 then hit30m cancellation before release completion; workload diagnosis is separately pending; source and base-reproduction investigation tracked by analyzer-shadow-owner-startup-sweep-race. No unchanged rerun or pre-existing/runtime attribution. Nullable six-file diff stays frozen; the independently correct prerequisite must qualify before integration. Original Acceptance and complex candidate tests remain unchanged.


- `nullable-original-probe-final.log`: admitted scratch compiler probe links EXACT candidate helper source and reconstructs original MinQualified emitter spelling. Thirteen represented methods/properties/event members: receiver Disable has 8 graph mismatches plus CS8632 before decoration; receiver Enable has 5 graph mismatches before decoration. Both serialized decorated cohorts compile and match all represented member graphs. This is helper evidence, not native public-route qualification.
- Same receipt: four proper raw ref/indexer/function-pointer/nullable-wrapper members compile and match under both receiving defaults; C#7.3 oblivious plain string stays legal, while nonoblivious decoration yields CS8370. Therefore the helper is independently useful before shared member projection, but legality checks cannot be deferred.
- Preserve candidate `Receiving_Context_Preserves_Recursive_Nullable_States`/`RecursiveNullableSource` unchanged: original emitters lose indexer keys and unsafe context, so that full public matrix remains parent closure after corresponding emitter repair. Add a distinct native specimen for representable original-emitter positions; do not trim the existing candidate test.
- Dependency is delivery order after namespace child. Cold nullable approval is conditional on explicit emitted-position mapping plus new-decoration compiler legality. Root alone writes canonical rows/deps; original parent `interface-extraction-nullability-context-fidelity` stays open and retains complete semantic/public-test prerequisites. Full receiving-language validation remains separate.
