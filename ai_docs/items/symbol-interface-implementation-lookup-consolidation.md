# symbol-interface-implementation-lookup-consolidation — one shared interface-implementation helper

**row:** `symbol-interface-implementation-lookup-consolidation` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/ParameterObjectService.cs:597`
- `src/RoslynMcp.Roslyn/Helpers/SymbolServiceHelpers.cs:89`
- `src/RoslynMcp.Roslyn/Services/ChangeSignatureAddRemovePreviewBuilder.cs:205`
- `src/RoslynMcp.Roslyn/Services/ReferenceService.cs:525`

## Acceptance

- [ ] A single shared helper in `SymbolServiceHelpers` exposes "does this member implement an interface member" / "which interface members does it implement", and `ParameterObjectService`, `ChangeSignatureAddRemovePreviewBuilder`, and `ReferenceService` all call it instead of their private loops.
- [ ] Existing interface-implementation behaviour tests for references and change-signature stay green; no per-service copy of the `AllInterfaces`/`FindImplementationForInterfaceMember` loop remains.

## Evidence

Traced by grep across `src/`: the identical `AllInterfaces` -> `GetMembers` -> `FindImplementationForInterfaceMember` + `SymbolEqualityComparer` pattern exists at `SymbolServiceHelpers.cs:89`, `ChangeSignatureAddRemovePreviewBuilder.cs:205`, `ReferenceService.cs:525/544/563`, and PR #1263 added a fourth private copy at `ParameterObjectService.cs:597`.

## Context

Surfaced by the code-quality reviewer on `parameter-object-target-method-contract-validation` (sweep 20260818T211226Z, PR #1263) as a medium-severity duplication finding. Advisory — did not block that PR.
