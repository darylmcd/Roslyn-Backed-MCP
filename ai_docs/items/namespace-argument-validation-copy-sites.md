# namespace-argument-validation-copy-sites — Validate and escape namespace strings outside change_type_namespace

**row:** `namespace-argument-validation-copy-sites` · **pri:** `Medium` · **size:** `M` · **verified:** `12bc2c8b5395056e11d2772b593347500033e5b6`

## Anchors

- `src/RoslynMcp.Roslyn/Services/FileOperationService.cs`:301,309,316-334 — folder names joined into a namespace (only spaces replaced) and parsed.
- `src/RoslynMcp.Roslyn/Services/CrossProjectRefactoringService.cs`:70-73 — caller `targetNamespace` only trimmed.
- `src/RoslynMcp.Roslyn/Services/StringLiteralReplaceService.cs`:212 — caller `UsingNamespace` (`A..B`) not validated.

## Acceptance

- [ ] Each producer refuses a malformed namespace before storing a token, and escapes keyword segments (`class` -> `@class`) or folder names that are not identifiers, with one shared helper (reuse `NamespaceNameIdentity`/`ReceivingNamespaceFactory`).
- [ ] Red-first tests through each public preview: folder `class` and `1x` for move_file, `A..B` for the other two.

## Evidence

- reloc-b report (PR 2144), sketch 6: "malformed StringLiteralReplacementDto.UsingNamespace (A..B) unvalidated; FileOperationService.cs:301,309 unescaped ParseName; CrossProjectRefactoringService.cs:70-73 unvalidated targetNamespace".
- Verified at 12bc2c8b: ComputeTargetNamespace (FileOperationService.cs:333) only replaces spaces.

## Context

- `relocation-destination-namespace-syntax-validation` covers change_type_namespace_preview only.
