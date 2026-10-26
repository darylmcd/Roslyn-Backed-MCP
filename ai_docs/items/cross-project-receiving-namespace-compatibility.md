# cross-project-receiving-namespace-compatibility — Preserve receiving namespace syntax compatibility

**row:** `cross-project-receiving-namespace-compatibility` · **pri:** `High` · **size:** `S` · **deps:** `interface-extraction-base-constraint-token-boundary`

## Anchors

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
