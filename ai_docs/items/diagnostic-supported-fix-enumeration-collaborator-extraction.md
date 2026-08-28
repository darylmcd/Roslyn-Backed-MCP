# diagnostic-supported-fix-enumeration-collaborator-extraction — Extract supported-fix enumeration

**row:** `diagnostic-supported-fix-enumeration-collaborator-extraction` · **pri:** `Low` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/DiagnosticService.cs` (`GetSupportedFixesAsync`, `CaptureRegisteredActionsAsync`, `GetFixGuidance`)
- `src/RoslynMcp.Roslyn/Services/SupportedFixEnumerationService.cs` (new internal collaborator)
- `tests/RoslynMcp.Tests/DiagnosticFixIntegrationTests.cs`

## Acceptance

- [ ] Move provider lookup, action capture, completeness aggregation, redacted failure reporting, and guidance projection behind one internal collaborator; keep `DiagnosticService` responsible for diagnostic lookup and DTO assembly.
- [ ] Preserve healthy partial fixes, combined failed-provider counts, cancellation propagation, and the additive `DiagnosticDetailsDto` wire shape.
- [ ] One table-driven regression covers healthy, throwing, mixed, and canceled provider outcomes through the collaborator boundary.

## Evidence

Current semantic metrics report `DiagnosticService.GetSupportedFixesAsync` at 74 LOC, cyclomatic complexity 14, nesting depth 3, and maintainability index 40.11 after completeness handling landed; the method now mixes provider discovery, document resolution, execution, deduplication, observability, and public guidance.
