# interface-extraction-receiving-language-reference-view — Language parity check over union receiving+source references

**row:** `interface-extraction-receiving-language-reference-view` · **pri:** `Medium` · **size:** `M` · **deps:** `interface-extraction-receiving-language-compatibility`

## Anchors

- `src/RoslynMcp.Roslyn/Helpers/InterfaceNullableProjection.cs`
- `src/RoslynMcp.Roslyn/Services/CrossProjectRefactoringService.cs`
- (new) `tests/RoslynMcp.Tests/InterfaceReceivingLanguageRouteTests.cs`

## Acceptance

- [ ] Cross-project: compile R against receiving references plus source references (union preserving alias/property views) plus the source compilation as a metadata reference, keeping the receiving project's own trees, so feature diagnostics are not masked by unresolved types.
- [ ] A case where an unresolved type previously masked a feature error is now refused.
- [ ] Project-reference alias and same-assembly internal accessibility controls pass.

## Evidence

- Split from `interface-extraction-receiving-language-compatibility` (bullet 6). The first slice diffs over identical references, which cancels unresolved-symbol errors but can miss a feature error masked by an unresolved type.

## Notes

PR 2018 review: B is built by re-parsing sibling trees under the SOURCE project's preprocessor symbols; a sibling declared only under a receiver-side or source-side #if symbol can skew R vs B (possible false refusal). Cover this and the unresolved-type masking case here; also the mixed-feature route row pins a single id because the compiler keys the id on version.
