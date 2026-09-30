# logging-event-id-collisions-no-catalog — Give every LoggerMessage a unique EventId and publish the catalog

**row:** `logging-event-id-collisions-no-catalog` · **pri:** `Medium` · **size:** `L` · **deps:** `logging-jsonl-drops-structured-state`

## Anchors

- `src/RoslynMcp.Roslyn/Services/CodeActionService.cs`
- `src/RoslynMcp.Roslyn/Helpers/DotnetCommandRunner.cs`
- `src/RoslynMcp.Roslyn/Services/GatedCommandExecutor.cs`
- `src/RoslynMcp.Roslyn/Services/SupportedFixEnumerationService.cs`
- `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs`
- `src/RoslynMcp.Host.Stdio/Diagnostics/TransportDisconnectDiagnostics.cs`
- `tests/RoslynMcp.Tests/ServerObservabilitySinkTests.cs`

## Acceptance

- [ ] One `LogEvents` constants class (ranges per subsystem) owns every EventId; no two events share an id.
- [ ] A drift test enumerates the catalog via reflection and fails on duplicate ids or a renamed/removed code.
- [ ] docs/stdio-client-integration.md publishes the code table (published contract).
- [ ] All six colliding definitions use catalog ids.

## Evidence

- Logging audit 20260930-1340 (dimension A2/A12); live-verified against HEAD 123ffd3a — see `ai_docs/audits/20260930-1340/report.md`.

## Context

EventId(1, …) is reused by LogProviderExecutionFailed, LogTransportDisconnected, LogCommandExecuted, LogProcessTreeKillFailed, LogProviderEnumerationFailed and LogWorkspaceLoaded; the file sink records only the numeric id, so `eventId==1` matches six unrelated occurrences (V7 log: GatedCommandExecutor and WorkspaceManager both emit eventId 1). Only the tool lifecycle (2101-2104) and UnexpectedFailure (1001) have deliberate codes; there is no in-repo catalog and the drift test (ServerObservabilitySinkTests) does not pin any event code.

Follow-on logging-event-ids-remaining-call-sites covers the 51 zero-id direct call sites.

**Approach:** Constants class in RoslynMcp.Core (shared by both assemblies); replace inline `new EventId(1, …)`; reflection-based uniqueness test; generate the doc table from the catalog in the same test or a just recipe.
