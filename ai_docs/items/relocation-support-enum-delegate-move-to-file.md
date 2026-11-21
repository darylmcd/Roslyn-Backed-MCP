# relocation-support-enum-delegate-move-to-file — BLOCKED: operator confirmation: stable move_type_to_file_preview refusal -> success counts as 5.x Added (docs/release-policy.md does not cover it)?

**row:** `relocation-support-enum-delegate-move-to-file` · **pri:** `Medium` · **size:** `M` · **deps:** `relocation-support-enum-delegate-types,relocation-support-enum-delegate-move-to-project`

## Anchors

- `src/RoslynMcp.Roslyn/Services/TypeMoveService.cs`
- `src/RoslynMcp.Roslyn/Helpers/UnsupportedTypeKindRefusal.cs`
- `tests/RoslynMcp.Tests/TypeMoveTests.cs`
- `tests/RoslynMcp.Tests/UnsupportedTypeKindRefusalTests.cs`

## Acceptance

- [ ] move_type_to_file_preview moves an enum or delegate to a new file and the result compiles; TypeMoveTests refusal tests (~:309-357) flipped to success+compile; AnyTypeDeclaration deleted (no remaining user after slices 1 and 2); dual-era raw-wire before/after evidence (2025-11-25 and 2026-07-28); one-line release-policy clarification that accepting previously refused input on a stable tool is additive.
- [ ] Changelog fragment category Added.

## Evidence

- Plan and cold review of slice 1: the tool is stable (ServerSurfaceCatalog.Refactoring.cs:26) and its refusal has shipped (CHANGELOG.md:1848). Planner and reviewer read refusal -> success as additive/minor; the counter-reading (published error text removed) would make it major with no major scheduled. Operator confirmation pending.
