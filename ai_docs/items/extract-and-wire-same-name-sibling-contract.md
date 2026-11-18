# extract-and-wire-same-name-sibling-contract — Make extract_and_wire honour the implemented-contract coverage rule

**row:** `extract-and-wire-same-name-sibling-contract` · **pri:** `Medium` · **size:** `M` · **deps:** `extract-interface-same-name-unrelated-contract-noop`

## Anchors

- `src/RoslynMcp.Roslyn/Services/CrossProjectRefactoringService.cs` (`DetectDuplicateInterfaceAsync`)
- (new) `src/RoslynMcp.Roslyn/Helpers/ImplementedInterfaceContract.cs`
- (new) `src/RoslynMcp.Roslyn/Helpers/SiblingInterfaceRepin.cs`
- `tests/RoslynMcp.Tests/OrchestrationIntegrationTests.cs`

## Acceptance

- [ ] `extract_and_wire_interface_preview` no-ops only when the implemented same-name interface covers the requested members (`ImplementedInterfaceContract`), and generates the interface with the sibling preserved otherwise. It refuses only a demonstrated target conflict. Match on `AllInterfaces` plus arity instead of `Interfaces` by name.
- [ ] Fix the `projectHint` handling on that route.
- [ ] Re-point the OrchestrationIntegrationTests cases that pin the old refusal-by-name to a fixture where `DetectExistingTypeConflictAsync` throws. Add red-first tests with pending and applied identities and a compile check.
- [ ] Route a re-pin-caused gate failure to `SiblingRemedy` instead of `ReceivingProjectRemedy`.
- [ ] One changelog fragment bullet with a Migration clause for the changed refusal behaviour.
- [ ] Optional last step: rename `GeneratedInterfaceReference`, whose name is stale for non-generated use.

## Evidence

- B1 of the parent row (standalone extract_interface route) added the shared coverage helper and re-pin, and left extract-and-wire untouched: it still refuses by name. The shared re-pin already protects that route's rebinding at HEAD.

## Context

- Same-mechanism instance of `extract-interface-same-name-unrelated-contract-noop` (name-only shortcut before checking the member contract).
