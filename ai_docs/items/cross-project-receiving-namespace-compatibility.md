# cross-project-receiving-namespace-compatibility — Preserve receiving namespace syntax compatibility

**row:** `cross-project-receiving-namespace-compatibility` · **pri:** `High` · **size:** `L` · **deps:** `raw-move-file-header-ownership`

## Anchors

- `src/RoslynMcp.Roslyn/Services/ClassSplitOrchestrator.cs` (`CreateCompilationUnit`)
- `src/RoslynMcp.Roslyn/Services/SymbolRefactorService.cs` (`EmitPartitionFiles`, `BuildPartitionFile`, `WrapInNamespace`)
- `src/RoslynMcp.Roslyn/Services/NamespaceRelocationService.cs` (`BuildMovedTypeCompilationUnit`)
- `src/RoslynMcp.Roslyn/Helpers/ReceivingNamespaceFactory.cs` (new required-options selector)

- `src/RoslynMcp.Roslyn/Services/CrossProjectRefactoringService.cs` (`CreateCompilationUnitForMember`, `CreateInterfaceCompilationUnit`, all producers and move-type)
- `tests/RoslynMcp.Tests/CrossProjectReceivingNamespaceIntegrationTests.cs` (new native public-route matrix)
- `tests/RoslynMcp.Tests/CrossProjectRefactoringIntegrationTests.cs` (existing move/extraction consumers)

## Acceptance

- [ ] Choose supported namespace syntax from the actual receiving project's effective parse options in BOTH shared-wrapper branches: block below C#10, preserve existing file-scoped output at C#10+, leave global namespace unwrapped. Preserve compiler policy; update every helper caller through required arguments, without defaulted shims or duplicated selection logic.
- [ ] Prove valid-before source and old-source unsupported namespace failures through cross-project extraction, dependency inversion, extract-and-wire and move-type; actual serialized preview and applied/reloaded output compile and bind under C#7.3/C#9 receivers after repair, with runtime behavior preserved.
- [ ] Preserve newer-language C#10/C#14 preview and moved-member bytes, existing assertions, branch-specific normalization, source/member comments/directives/literal trivia, header/EOF ownership and unrelated bytes. Older move wrappers add only required brace boundaries without normalizing the member or duplicating trivia; trailing comments cannot consume the closing brace.
- [ ] Cover namespace/no-namespace and move namespace-selection controls. Preserve source disk before apply, workspace/preview ownership and project options; apply only authorized source removal/reference changes. Require correct native options and zero native load errors.
- [ ] Keep semantic parameter/ref/unsafe/escaped projection, nullable decoration and full receiving-language validation in their existing owners; preserve every existing parent Acceptance and candidate complex test.

## Evidence

- Original source `0e0f90661035a38e570627b1508dac2ea2496708`: both wrapper branches unconditionally create file-scoped namespaces. Cold reviewer `reviewer-namespace-probe-final.log` used admitted reflection/compiler probes: BOTH move/interface branches report C#7.3 CS8370 and C#9 CS8773; C#14 has no errors. These establish the defect, not child public-route qualification.
- Revised scratch `semantic-split-plan.md` supersedes rejected normalized-only repair. Minimal complete closure: one shared receiving-options decision, both wrapper branches and all three producer callsites in the same service file; retain move trivia and C#10+ output.
- Dependency is approved delivery order after the base writer; no semantic projection API dependency. Root owns canonical writing and closure; implement only after dependency/decomposition docs land and base PR1842 merges. Parent `interface-extraction-receiving-language-compatibility` remains open and depends on this child plus its complete semantic prerequisites.

- Header-role probe: admitted move-generated-context-probe-final.log proves current and original wrappers move a global auto-generated banner after the new namespace. Actual Nullable Enable changes generated=True/None annotations to generated=False/NotAnnotated.
- Repair file-header placement before the namespace; retain selected XML docs/type body on their declaration and unrelated docs in source.
- Assert file-header and type-owned trivia separately. Raw member text can contain a file header and cannot alone prove ownership.
- Cold review requires first-type removal/remainder and explicit nullable-directive controls; this same-helper closure cannot move to the nullable-projection row.

## Scope

- Four unconditional namespace emitters share one unsupported-syntax mechanism. Extend the same required actual receiving-options selector to CrossProject, ClassSplit, SymbolRefactor partitions and NamespaceRelocation; preserving-kind InterfaceExtraction and TypeExtraction are verified safe controls.
- Depends on raw-move-file-header-ownership. Preserve all existing Acceptance, all 45 candidate cases and caller-specific normalization/trivia; add native old-language red/green and modern/global controls for the other three emitters. Five production files are the cohesive closure, not a reason to leave copies behind.
- Raw header/removal ownership lands first; semantic parameter/ref/unsafe/escaped and nullable parents remain open. Original ten-row selection remains intact; the added header child is an explicitly forced prerequisite.
