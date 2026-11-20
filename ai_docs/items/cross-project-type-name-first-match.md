# cross-project-type-name-first-match — Require an unambiguous cross-project type target

**row:** `cross-project-type-name-first-match` · **pri:** `Medium` · **size:** `M` · **deps:** `release-policy-correction-rule-adr`

## Anchors

- `src/RoslynMcp.Roslyn/Services/CrossProjectRefactoringService.cs` — FindTypeDeclaration and its move, extract-interface and dependency-inversion callers.
- `src/RoslynMcp.Host.Stdio/Tools/CrossProjectRefactoringTools.cs` — three experimental previews accept a source file and typeName.
- `tests/RoslynMcp.Tests/CrossProjectInterfaceBindingIntegrationTests.cs` — nested and same-file decoy fixture.

## Acceptance

- [ ] Determine selection behavior for same-file containing types, generic arity and repeated declarations of the same logical type before implementation. Drive the three experimental tools through their MCP routes.
- [ ] Reject ambiguous simple names without choosing by enumeration order. Supply a supported way to address a specific declaration and preserve ordinary unique-name calls.
- [ ] Recheck stable shared tool schemas, refusal serialization and published payloads before selection. Use the next major only if a complete correction changes a stable contract.

## Evidence

- 5.x review: FindTypeDeclaration uses FirstOrDefault on Identifier.ValueText. All three experimental previews share it. The nested dependency-inversion fixture puts the intended containing type first, and tests post-selection binding. There is no wire evidence that distinguishes competing same-file targets.

## Notes

Operator decision 2026-11-20: corrections are minor. Unparked. Qualify this row against the amended docs/release-policy.md (row release-policy-correction-rule-adr) with before/after wire evidence: same response shape and error categories with an incorrect result made correct ships in 5.x as Fixed; a changed shape, category or published message text re-parks the row as major-only with the evidence quoted.
