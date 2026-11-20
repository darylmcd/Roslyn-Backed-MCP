# not-found-metadata-only-message-classification — Classify metadata-only lookup failures from typed evidence

**row:** `not-found-metadata-only-message-classification` · **pri:** `Low` · **size:** `M` · **deps:** `release-policy-correction-rule-adr`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/ToolErrorHandler.cs:549-569`
- `src/RoslynMcp.Roslyn/Services/SymbolNavigationService.cs:93`
- `src/RoslynMcp.Roslyn/Services/TypeConsumersService.cs:52-56`
- `tests/RoslynMcp.Tests/GoToTypeDefinitionTests.cs`

## Acceptance

- [ ] Classify actual metadata-only symbol failures by producer evidence while ordinary misses remain ordinary even when caller text contains the classifier phrase. Preserve the existing safe envelope for actual metadata-only results, and qualify stable callers across the supported protocol eras before deciding release impact.

## Evidence

- Live 5.0.1 find_type_consumers returned generic NotFound for SyntheticMissingTypeScreen and a misleading metadata-only NotFound for SyntheticMissingType-metadata-only-Screen. BuildSafeNotFoundMessage searches the entire KeyNotFoundException message, which can contain caller input. GoToTypeDefinitionTests pin the legitimate metadata-only message.

## Notes

- Release impact uncertain. Re-screen other stable KeyNotFoundException producers before selection. If correcting their observable messages requires a major, include the ADR, migration and both raw-wire eras at the next major.

Operator decision 2026-11-20: corrections are minor. Unparked. Qualify this row against the amended docs/release-policy.md (row release-policy-correction-rule-adr) with before/after wire evidence: same response shape and error categories with an incorrect result made correct ships in 5.x as Fixed; a changed shape, category or published message text re-parks the row as major-only with the evidence quoted.
