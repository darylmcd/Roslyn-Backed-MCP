# extract-type-preview-compile-delta-gate — Refuse extract_type previews that introduce compiler errors

**row:** `extract-type-preview-compile-delta-gate` · **pri:** `High` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/TypeExtractionService.cs:154-162` — token stored with no compilation-diagnostics comparison.

## Acceptance

- [ ] Red first: static-class (CS0708/CS0710), partial-pair (CS0751) and abstract-override (CS0534) fixtures currently return preview tokens.
- [ ] Diff error diagnostics per document keyed on id and location (reuse the identity/multiplicity differential in `InterfaceNullableProjection.AddedErrors`); list every added id, no truncation.
- [ ] Refusal leaves no token, workspace-version change or disk effect.

## Evidence

- 2026-10-27: CS0708, CS0710 and CS0751 observed on main's rendered output; CS0534 by reasoning from `tests/RoslynMcp.Tests/TypeExtractionTests.cs:492-549`.

## Context

- A compile gate is a safety net, not proof of preservation: rows `extract-type-composition-assigned-before-use` through `extract-type-late-bound-member-identity` still own behaviour changes that compile.
