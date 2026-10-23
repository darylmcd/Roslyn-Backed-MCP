# cross-project-interface-signature-dependencies — Validate generated interface signature dependency closure

**row:** `cross-project-interface-signature-dependencies` · **pri:** `High` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/CrossProjectRefactoringService.cs:169`
- `src/RoslynMcp.Roslyn/Services/CrossProjectRefactoringService.cs:776`
- `src/RoslynMcp.Roslyn/Services/CrossProjectRefactoringService.cs:817`
- `src/RoslynMcp.Roslyn/Helpers/ProjectGraphHelpers.cs:8`
- `tests/RoslynMcp.Tests/CrossProjectRefactoringIntegrationTests.cs:441`
- `tests/RoslynMcp.Tests/CrossProjectRefactoringIntegrationTests.cs:538`
- `tests/RoslynMcp.Tests/OrchestrationIntegrationTests.cs:548`

## Acceptance

- Determine dependencies from every emitted signature type, including nested generic arguments, arrays, method constraints, properties and events; preserve original symbol identity in the target compilation.
- Include required existing project/metadata dependencies only when the resulting source/target/consumer graph is valid; preserve reference aliases and metadata.
- Refuse unsupported or cyclic signature closure before publishing a preview token; do not add a reverse edge that cycles with the source implementation edge or silently relocate user types.
- Cover direct extraction, dependency inversion and extract-and-wire through the shared mechanism.
- Preserve existing namespace/trivia assertions; use a valid acyclic signature-owner fixture for successful using-generation controls and add source-owned-signature cycle refusal controls.
- Assert generated signatures bind to the expected symbols and compile, rather than only parsing syntax. Observe causal failure before the fix.

## Evidence

| Observation | Evidence |
|---|---|
| Generated declaration imports namespaces but does not establish signature dependencies | CollectReferencedNamespaces returns namespace strings only; AddExtractedInterfaceAsync adds source-to-target reference only. |
| Source-owned signature cannot be repaired with a reverse reference alone | SampleLib.AnimalService emits List<SampleLib.IAnimal>; after SampleLib-to-Contracts, Contracts-to-SampleLib creates a cycle. |
| Existing successful test misses semantic failure | Extract_Interface_Cross_Project_Emits_Using_For_Source_Project_Types verifies using SampleLib.Hierarchy and CSharpSyntaxTree.ParseText diagnostics; Shape remains declared in SampleLib. |
| Generic and orchestration companions exercise the same closure | Generic Shape arguments and AnimalService/IAnimal require the signature owner's assembly in Contracts. |
| Generated interface symbol existence is insufficient | ExtractedInterfaceReference.ReadAsync obtains the interface declaration symbol without checking its member signature binding. |
| Mechanism predates current namespace changes | Read-only git show HEAD at e5edef1272822a4b1edca417d42dbaa67cf5c59b shows both extraction routes collect namespaces and add only source-to-target references. |
| Runtime limits | Static source review only; no build/test executed for this finding. No compiler error code claimed as observed. |
| Tracking search | No existing open item found for signature dependency closure/cycles. Existing cross-project-interface-reference-binding covers references to the generated interface; generic-type-parameters covers declaration parameters. |

## Scope

- Distinct from qualifying the generated interface's own name and from preserving generic declaration parameters.
- No batch selection, product edit or implementation authorization implied by this payload.
