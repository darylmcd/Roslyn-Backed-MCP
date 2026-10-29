# extract-type-member-modifier-dispatch-duplication — Consolidate repeated Method/Property/Field/Event modifier dispatch in TypeExtractionService (silent `_ => member` fallthrough)

**row:** `extract-type-member-modifier-dispatch-duplication` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/TypeExtractionService.cs` (GetMemberModifiers, WithMemberModifiers, EnsurePublicAccessibility, WidenIfReferencedByRetainedCode)

## Acceptance

- [ ] One member-kind dispatch helper; unsupported member kinds fail loudly instead of returning the member unchanged; existing TypeExtractionTests pass.

## Evidence

- Seen while implementing extract-type-override-modifier-stripped.
