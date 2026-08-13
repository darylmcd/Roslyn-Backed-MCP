# type-extraction-member-shape-validation — Refuse unsafe extraction member shapes

**row:** `type-extraction-member-shape-validation` · **pri:** `High` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/TypeExtractionService.cs`
- `tests/RoslynMcp.Tests/TypeExtractionTests.cs`

## Acceptance

- [ ] Define and validate the supported extraction member shapes before constructing a preview.
- [ ] Refuse constructors so a constructor named for the source type is never emitted unchanged into the new type.
- [ ] Split multi-declarator fields to move only requested variables, or refuse that shape explicitly.
- [ ] Refuse name-ambiguous method overload selection with structured candidates, or require an explicit all-overloads selection; source order must never choose silently.
- [ ] Add one regression group proving no preview emits a mismatched constructor, moves an unrequested sibling field, or silently selects one overload.

## Evidence

- `GetMemberName` admits constructors and maps a multi-variable field through only its first declarator; `PartitionMembers` removes a requested name after the first declaration, silently retaining later overloads.
