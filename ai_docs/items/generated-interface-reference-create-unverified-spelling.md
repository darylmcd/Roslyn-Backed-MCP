# generated-interface-reference-create-unverified-spelling — Verify every generated-interface spelling by binding

**row:** `generated-interface-reference-create-unverified-spelling` · **pri:** `Medium` · **size:** `M` · **verified:** `ecde13a0b03ee7ebc6db18d3886b352375d5cf40`

## Anchors

- `src/RoslynMcp.Roslyn/Helpers/GeneratedInterfaceReference.cs`
- `src/RoslynMcp.Roslyn/Services/InterfaceExtractionService.cs`
- `src/RoslynMcp.Roslyn/Services/CrossProjectRefactoringService.cs`

## Acceptance

- [ ] Move every `GeneratedInterfaceReference.Create` caller to `TryCreate` (introduced by the same-name sibling fix): the replaceUsages retype in `InterfaceExtractionService`, the dependency-inversion constructor retype in `CrossProjectRefactoringService`, and `AddToBaseListAsync`. Refuse with file, line and "cannot be re-pinned" when no spelling binds to the generated interface. Then delete `Create`.
- [ ] Red-first test per caller using an extern-alias fixture: the source project is reachable only under an extern alias, and another assembly defines a type with the same fully qualified name under the global alias. Mutation-check each by making the final bind return true.

## Evidence

- `Create` still returns an unchecked final `global::` spelling. When an extern alias hides the namespace, or another type shares the fully qualified name, the written text either compiles and means a different type (a declared but unassigned field or property still compiles) or fails and is visible only as a gate error.
- Found by cold review of PR 2148 (`SiblingInterfaceRepin` already uses `TryCreate`).

## Context

- Chained on the same-name sibling fix. `CrossProjectRefactoringService.EnsureProjectReference` partly mitigates the alias-only edge by making it globally visible.
