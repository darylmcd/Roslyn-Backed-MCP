---
category: Maintenance
---

- **Maintenance:** Corrected ADR 0001 (`docs/decisions/0001-locationdto-nested-field-migration.md`) — the location-quartet producer survey is now scoped per DTO instead of conflating the three sets into one count: eight `DiagnosticDto` producers (was five — `ScriptingService.cs`, `SnippetAnalysisService.cs`, `UnresolvedAnalyzerReferenceStripper.cs`, and `WorkspaceDiagnosticsSink.cs` were omitted), one `SymbolDto` producer (`SymbolMapper.cs`, which double-duties as a `DiagnosticDto` producer and is the sets' only overlap), and one `TypeUsageDto` producer (`MutationAnalysisService.cs`), for nine distinct files in union. Stage 1's scoping bullet no longer relies on a `DiagnosticDto`-only `rg`, which would have silently missed the sole `TypeUsageDto` producer. Section 2 now states the symmetric null-location rule for producers that resolve line/column but no file path. Closes `adr-0001-location-migration-corrections`.
