# logging-event-ids-remaining-call-sites — Assign catalog EventIds to the remaining zero-id log call sites

**row:** `logging-event-ids-remaining-call-sites` · **pri:** `Low` · **size:** `L` · **deps:** `logging-event-id-collisions-no-catalog`

## Anchors

- `src/RoslynMcp.Roslyn/Services/ReferenceService.cs`
- `src/RoslynMcp.Roslyn/Services/SymbolRelationshipService.cs`
- `src/RoslynMcp.Roslyn/Services/UnusedCodeAnalyzer.cs`
- `src/RoslynMcp.Roslyn/Services/SymbolSearchService.cs`
- `src/RoslynMcp.Roslyn/Services/SymbolNavigationService.cs`
- `src/RoslynMcp.Roslyn/Services/FileOperationService.cs`
- `src/RoslynMcp.Roslyn/Helpers/AnalyzerReferenceIsolation.cs`
- `src/RoslynMcp.Roslyn/Services/ScriptWorkerProcess.cs`
- `src/RoslynMcp.Roslyn/Services/FileWatcherService.cs`
- `src/RoslynMcp.Roslyn/Services/ExtractMethodService.cs`
- `src/RoslynMcp.Host.Stdio/Services/NuGetVersionChecker.cs`
- `src/RoslynMcp.Host.Stdio/Middleware/GetPromptErrorFilter.cs`
- `src/RoslynMcp.Host.Stdio/Middleware/StructuredWorkspaceResolver.cs`
- `src/RoslynMcp.Host.Stdio/Middleware/StructuredCallElicitationCoordinator.cs`

## Acceptance

- [ ] No `Log*("literal"` call without an EventId remains in src/ (enforced by a test or analyzer rule).
- [ ] Each site uses a catalog constant from logging-event-id-collisions-no-catalog.

## Evidence

- Logging audit 20260930-1340 (dimension A2); live-verified against HEAD 123ffd3a — see `ai_docs/audits/20260930-1340/report.md`.

## Context

rg census: 51 `LogXxx("literal …")` calls pass no EventId (→ eventId 0 in the sink), e.g. "Tool symbol_search called without workspaceId; resolved to the single loaded workspace …" (V1 log). A zero id is unenumerable, so agents can only grep English wording that drifts with copy edits.

**Approach:** Mechanical conversion per subsystem using catalog ranges; add the enforcing test first (red-first). L by anchors: expect the remediation engine to split by subsystem.
