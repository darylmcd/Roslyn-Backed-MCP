# fix-all-scope-public-argument-refusals — Publish FixAll scope argument corrections

**row:** `fix-all-scope-public-argument-refusals` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/FixAllTargetResolver.cs:25`
- `tests/RoslynMcp.Tests/FixAllServiceIntegrationTests.cs`
- (new) `tests/RoslynMcp.Tests/FixAllArgumentRefusalWireTests.cs`

## Acceptance

- [ ] ParseAndValidate returns authored public corrections for invalid scope and scope-required filePath/projectName; name valid scopes and the actual parameter without echoing caller free text, paths or lower-layer errors. Preserve released category and ArgumentException identity.
- [ ] Observe old-behavior failing production-boundary regressions for all three guards and fixed passing results; retain document/project/solution resolution and hostile-input redaction in the complete envelope.

## Evidence

- At main `6cf842a8b7ea8ac56d7706e9c91c451d1852d9b4`, `FixAllTargetResolver.cs:25-40` constructs plain ArgumentException for invalid scope and missing scope-dependent inputs; the invalid-scope message interpolates caller scope.
- This production anchor was in the parent's Anchors despite having no dedicated Acceptance bullet; preserve its tracked work during decomposition.

## Context

- Split from `code-action-and-flow-argument-refusals-public-message`; one scope-argument validation mechanism, independent of action-index and flow-region correction.
- Publication contract: `src/RoslynMcp.Core/Services/PublicArgumentException.cs`, `ArgumentErrors.cs`; check current host boundary and `docs/release-policy.md`. Do not rely on the deleted core-move detail.
- backlog: sync ai_docs/backlog.md.
