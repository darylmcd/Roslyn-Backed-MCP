---
category: Maintenance
---

- **Maintenance:** Corrected ADR 0001 (`docs/decisions/0001-locationdto-nested-field-migration.md`) — the `DiagnosticDto` producer survey now lists the actual eight producers instead of five (`MutationAnalysisService.cs` was never a producer; `ScriptingService.cs`, `SnippetAnalysisService.cs`, `UnresolvedAnalyzerReferenceStripper.cs`, and `WorkspaceDiagnosticsSink.cs` were omitted), and section 2 now states the symmetric null-location rule for producers that resolve line/column but no file path. Closes `adr-0001-location-migration-corrections`.
