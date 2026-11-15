# move-type-to-project-receiving-language-gate — move_type_to_project_preview stores tokens for types the receiving project's language version cannot compile

**row:** `move-type-to-project-receiving-language-gate` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/CrossProjectRefactoringService.cs` (`PreviewMoveTypeToProjectAsync` stores previews without a receiving compiler-diagnostics gate)
- `src/RoslynMcp.Roslyn/Helpers/InterfaceNullableProjection.cs` (`EnsureReceivingLanguageAsync`, to be shared)
- `src/RoslynMcp.Roslyn/Helpers/MovedTypeDestinationIdentity.cs` (`EnsureAvailableAsync`)
- (new) `tests/RoslynMcp.Tests/MoveTypeReceivingLanguageTests.cs`

## Acceptance

- [ ] Red first through experimental `move_type_to_project_preview`. Receiving projects with a lower LangVersion must refuse unsupported moved syntax before storing a token, reporting the compiler IDs without compiler text.
- [ ] Prove a declaration guarded by `#if` can be present in the source and disappear under the receiver's symbols without compiler errors. Refuse before storing a token when the full projection lacks the intended declaration. Keep an active receiving control and verify files and workspace state on refusal.
- [ ] Reuse the receiving-vs-source parse-option differential from the interface-extraction gate through one shared helper. Use the receiving compiler to verify that the emitted declaration is active.

## Evidence

- PR #2018 implementer probe: `TypeExtractionService.RefuseAddedCompilerErrorsAsync` is same-project and has no receiving language; the real sibling is `move_type_to_project_preview`, which has no compile check of any kind.

## Notes

The helper-level `DestinationIdentity_DoesNotReportCollisionForAnInactiveDeclaration` control verifies a distinct receiving projection gap. The source parse options can activate a declaration behind `#if MOVE_PROBE_ACTIVE` while target symbols suppress the same emitted declaration. This case creates no receiver identity, so the collision guard correctly has no type to compare. Include a red-first public preview where the intended receiving declaration is absent, with no new compiler error to find, when designing the shared receiving-admissibility gate. Prove no token is minted and both projects remain unchanged. A lower LangVersion and a missing declaration need independent controls.
