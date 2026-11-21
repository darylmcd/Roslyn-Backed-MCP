# relocation-support-enum-delegate-move-to-project — Support enum and delegate types in move_type_to_project_preview (slice 2 of 3)

**row:** `relocation-support-enum-delegate-move-to-project` · **pri:** `Medium` · **size:** `S` · **verified:** `5b38ef3946695ab1209e83a952b0110e81f50a61` · **deps:** `relocation-support-enum-delegate-types`

## Anchors

- `src/RoslynMcp.Roslyn/Services/CrossProjectRefactoringService.cs`
- `tests/RoslynMcp.Tests/CrossProjectUnsupportedKindRefusalTests.cs`
- `tests/RoslynMcp.Tests/CrossProjectRefactoringIntegrationTests.cs`

## Acceptance

- [ ] move_type_to_project_preview moves an enum or delegate to another project and the result compiles (preserveNamespace:true: the tool never rewrites consumers for any kind), red-first test per kind; the move route is dropped from CrossProjectUnsupportedKindRefusalTests; dependency_inversion_preview and extract_interface keep refusing kinds they cannot handle.
- [ ] Changelog fragment category Added.

## Evidence

- Plan of relocation-support-enum-delegate-types (slice 2), cold-reviewed 2026-11-21.
