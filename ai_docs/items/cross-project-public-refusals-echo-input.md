# cross-project-public-refusals-echo-input — Remove caller detail from public cross-project refusals

**row:** `cross-project-public-refusals-echo-input` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/CrossProjectRefactoringService.cs:55-79`
- `src/RoslynMcp.Roslyn/Services/CrossProjectRefactoringService.cs:132-174`
- `src/RoslynMcp.Roslyn/Services/CrossProjectRefactoringService.cs:221-348`
- `src/RoslynMcp.Roslyn/Services/CrossProjectRefactoringService.cs:812`
- `tests/RoslynMcp.Tests/CrossProjectRefactoringIntegrationTests.cs`

## Acceptance

- [ ] Replace every public interpolation of caller type/project names and relative destination paths with authored path/input-free corrections. Preserve category, BCL exceptionType, preview state and successful operations.
- [ ] Probe all PublicInvalidOperationException constructors and DescribeProjectRelativePath consumers in this service; remove dead formatting code after complete conversion. Distinguish caller input from valid resolved workspace symbols.
- [ ] Add failing-before/fixed-after service-to-wire regressions for unknown project/type inputs and existing destination refusals, including malicious path/secret sentinels and ordinary correction guidance. Check the complete serialized envelope and inner-cause ownership.
- [ ] Apply docs/release-policy.md to the currently published message change; record ADR and migration fragment when required. Gate on the correct release strategy, not cosmetic classification.

## Evidence

- Current-session inspection 2026-10-04: unresolved typeName is interpolated at lines 55/132/221/237/348; targetProjectName at 341; caller-derived relative destination paths at 79/174/812.
- These exceptions already implement IPublicMessageException; marker-first publication exposes the strings rather than generic redaction.

## Context

- Separate from file-create-and-scaffold-refusals-public-message: that row converts previously unmarked refusals and fixes copied containment predicates. This row repairs already-public free-text publication in cross-project operations.
- Existing relative-path-only comments at lines 329-334 describe weaker behavior than the path/input-free correction policy. Update them with the formatter removal.
