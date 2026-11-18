# sibling-repin-linked-copy-spelling-per-copy — Verify the repin spelling in each linked copy

**row:** `sibling-repin-linked-copy-spelling-per-copy` · **pri:** `Medium` · **size:** `M` · **verified:** `ecde13a0b03ee7ebc6db18d3886b352375d5cf40` · **deps:** `generated-interface-reference-create-unverified-spelling`

## Anchors

- (new) `src/RoslynMcp.Roslyn/Helpers/SiblingInterfaceRepin.cs`
- `src/RoslynMcp.Roslyn/Helpers/GeneratedInterfaceReference.cs`

## Acceptance

- [ ] For every same-text linked copy, run `TryCreate(original, that copy's own after model, span.Start)`. Refuse with file and line when it fails or returns text different from the first copy's.
- [ ] Red-first test: a linked project that has an extra same-named type, or an extern alias only on its side, so the spelling verified in the first copy binds elsewhere in the linked copy.

## Evidence

- The merge loop in `SiblingInterfaceRepin` writes a spelling that `TryCreate` verified only against the first copy's `after` model into every same-text linked copy. `BindsToAsync` only checks that the copy's baseline binding has the same display name as the original, not that the chosen spelling binds there.
- Found by cold review of PR 2148.

## Context

- Contrived shape; same mechanism as the unverified-spelling row `generated-interface-reference-create-unverified-spelling`.
