# interface-extraction-nested-signature-type-qualification — Preserve containing type qualification in emitted signatures

**row:** `interface-extraction-nested-signature-type-qualification` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/InterfaceExtractionService.cs` (BuildInterfaceMembers)
- `src/RoslynMcp.Roslyn/Services/CrossProjectRefactoringService.cs` (CreateInterfaceMember, CreateParameterList)
- `tests/RoslynMcp.Tests/ExtractInterfaceSemanticUsingsTests.cs`
- `tests/RoslynMcp.Tests/CrossProjectRefactoringIntegrationTests.cs`

## Acceptance

- [ ] Preserve enclosing type names and every substituted containing generic argument for all currently emitted signature positions in both generators; cover nested application types and closed framework nested types.
- [ ] Add failing-before/fixed-after public-route regressions with original-valid sources and already configured receiving references. Verify actual serialized preview, apply/reload compilation and bound type identities; do not substitute namespace imports for containing type qualification.
- [ ] Keep declaration generic parameters, omitted cross-project events and missing assembly/reference closure in their distinct rows. Do not expand those features to conceal the spelling defect.
- [ ] If an earlier semantic projection change appears to fix spelling, verify its exact merged head with the same native regressions before closing this row.

## Evidence

- Read-only split planning 2026-10-26 against original production found MinQualified output loses Outer<T> in Outer<T>.Inner<U> and Dictionary<TKey,TValue> in its ValueCollection. Exact original-emitter scratch compiler controls report Inner<U>/ValueCollection and CS0246 both with and without the nullable decorator; original-source recursive type names are valid. This is an independent pre-existing spelling mechanism, not a nullable-context regression.
- Current-session logs nullable-original-probe-final.log and semantic-split-plan.md retain the original-vs-decorated controls. Source audit anchors: original standalone BuildInterfaceMembers around249/256 and cross CreateInterfaceMember around606/613 plus CreateParameterList626; line positions move after the base-writer extraction.
- Preserved combined candidate may already repair this mechanism through semantic type projection. That source claim is not a native public-route receipt and does not close the row. Original signature-dependencies/generic-type-parameters objectives remain separate.
