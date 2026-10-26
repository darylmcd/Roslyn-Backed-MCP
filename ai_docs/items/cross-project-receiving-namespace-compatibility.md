# cross-project-receiving-namespace-compatibility — Preserve receiving namespace syntax compatibility

**row:** `cross-project-receiving-namespace-compatibility` · **pri:** `High` · **size:** `M` · **deps:** `raw-move-file-header-ownership,ci-artifact-owner-quality-deadline`

## Anchors

- `src/RoslynMcp.Roslyn/Services/CrossProjectRefactoringService.cs` (both wrappers and all direct/inversion/composite/move producers)
- `src/RoslynMcp.Roslyn/Helpers/ReceivingNamespaceFactory.cs` (new required actual-options selector)
- `tests/RoslynMcp.Tests/CrossProjectReceivingNamespaceIntegrationTests.cs` (retained45 plus boundary controls)
- `tests/RoslynMcp.Tests/ReceivingNamespaceFactoryTests.cs` (effective-language/required-options controls)
- `tests/RoslynMcp.Tests/CrossProjectRefactoringIntegrationTests.cs` (existing consumers)

## Acceptance

- [ ] Choose supported namespace syntax from the actual receiving project's effective parse options in BOTH shared-wrapper branches: block below C#10, preserve existing file-scoped output at C#10+, leave global namespace unwrapped. Preserve compiler policy; update every helper caller through required arguments, without defaulted shims or duplicated selection logic.
- [ ] Prove valid-before source and old-source unsupported namespace failures through cross-project extraction, dependency inversion, extract-and-wire and move-type; actual serialized preview and applied/reloaded output compile and bind under C#7.3/C#9 receivers after repair, with runtime behavior preserved.
- [ ] Preserve newer-language C#10/C#14 preview and moved-member bytes, existing assertions, branch-specific normalization, source/member comments/directives/literal trivia, header/EOF ownership and unrelated bytes. Older move wrappers add only required brace boundaries without normalizing the member or duplicating trivia; trailing comments cannot consume the closing brace.
- [ ] Cover namespace/no-namespace and move namespace-selection controls. Preserve source disk before apply, workspace/preview ownership and project options; apply only authorized source removal/reference changes. Require correct native options and zero native load errors.
- [ ] Keep semantic parameter/ref/unsafe/escaped projection, nullable decoration and full receiving-language validation in their existing owners; preserve every existing parent Acceptance and candidate complex test.

## Evidence

- PARKED PR1846 draft ata9df5cd36fb12625333c9ab837e8ab0996eb763e. Root exact388/0/0 and cold PASS retained; hosted passed2650cases then cancelled duringformat. Required CI prerequisite PR1849 remains draft/red due subsequent native PowerShell134 startup faults, not a demonstrated namespace defect. No unchanged retry, test weakening, source expansion or closure. Receiving-context and semantic parents remain dependent; do not adopt an unqualified CI prerequisite.


- Original source `0e0f90661035a38e570627b1508dac2ea2496708`: both wrapper branches unconditionally create file-scoped namespaces. Cold reviewer `reviewer-namespace-probe-final.log` used admitted reflection/compiler probes: BOTH move/interface branches report C#7.3 CS8370 and C#9 CS8773; C#14 has no errors. These establish the defect, not child public-route qualification.
- Revised scratch `semantic-split-plan.md` supersedes rejected normalized-only repair. Minimal complete closure: one shared receiving-options decision, both wrapper branches and all three producer callsites in the same service file; retain move trivia and C#10+ output.
- Dependency is approved delivery order after the base writer; no semantic projection API dependency. Root owns canonical writing and closure; implement only after dependency/decomposition docs land and base PR1842 merges. Parent `interface-extraction-receiving-language-compatibility` remains open and depends on this child plus its complete semantic prerequisites.

- Header-role probe: admitted move-generated-context-probe-final.log proves current and original wrappers move a global auto-generated banner after the new namespace. Actual Nullable Enable changes generated=True/None annotations to generated=False/NotAnnotated.
- Repair file-header placement before the namespace; retain selected XML docs/type body on their declaration and unrelated docs in source.
- Assert file-header and type-owned trivia separately. Raw member text can contain a file header and cannot alone prove ownership.
- Cold review requires first-type removal/remainder and explicit nullable-directive controls; this same-helper closure cannot move to the nullable-projection row.

## Scope

- First independently correct consumer: one required-options factory plus CrossProject raw/generated paths and every producer. Original five Acceptance bullets remain unchanged; retain the entire original45 matrix and modern/global byte contracts.
- Prior all-four-consumer Scope is explicitly decomposed under operator Directive6: ClassSplit -> class-split-receiving-namespace; SymbolRefactor -> split-service-receiving-namespace; NamespaceRelocation -> relocation-receiving-namespace. Each remainder depends on this first consumer; no extra owner is silently removed or claimed fixed.
- PR1846 is the first consumer; the three independent owners follow separately. Remainder rows are written before its merge as required by the current prompt. No half-migrated caller or unused-helper-only PR.
- Raw header prerequisite shipped PR1845 atca6f94dd; its ordinary closure stays in the one trailing docs PR. Broader nullable/member/language parents retain their original Acceptance and existing owners. Original count history is unchanged.
