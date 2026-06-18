# Backlog sweep plan — 20260618T143921Z

**Generated:** 2026-06-18T14:39:21Z
**Backlog snapshot:** 2026-06-11T20:00:00Z
**Initiative count:** 8
**Anchor verification:** pending

<!-- BSWEEP:STATUS-TABLE BEGIN — generated from state.json; do not edit by hand -->
## Status (generated)

| # | id | status | PR | rows closed |
|---|----|--------|----|-------------|
| 1 | signature-help-bare-null | pending | — | signature-help-bare-null |
| 2 | elicitation-retry-exception-envelope | pending | — | elicitation-retry-exception-envelope |
| 3 | workspace-id-omitted-residual-recovery-coherence | pending | — | workspace-id-omitted-residual-recovery-coherence |
| 4 | find-implementations-corlib-root-tighten-heuristic | pending | — | find-implementations-corlib-root-tighten-heuristic |
| 5 | workspace-validation-process-kill-failure-observability | pending | — | workspace-validation-process-kill-failure-observability |
| 6 | script-execution-supervisor-cancel-catch-observability | pending | — | script-execution-supervisor-cancel-catch-observability |
| 7 | nuget-version-checker-test-wallclock-poll | pending | — | nuget-version-checker-test-wallclock-poll |
| 8 | legacy-bug-id-tool-descriptions | pending | — | legacy-bug-id-tool-descriptions |
<!-- BSWEEP:STATUS-TABLE END -->

## Initiatives (in order)

### 1. signature-help-bare-null

| Field | Content |
|---|---|
| Diagnosis | `GetSignatureHelp` in `src/RoslynMcp.Host.Stdio/Tools/SymbolTools.cs` (line 567) calls `JsonSerializer.Serialize(result, ...)` unconditionally on the nullable return from `symbolRelationshipService.GetSignatureHelpAsync`. When the locator is unresolvable `result` is `null`, so the tool serializes a bare JSON `null` instead of the standard `{error, category:NotFound, message}` envelope. Root cause is the missing null-check before serialize — identical to the `member-hierarchy-bare-null` defect fixed at line 543. Both the `GetMemberHierarchy` (line 543) and `GetSymbolRelationships` (line 592) handlers now throw `KeyNotFoundException` on null; `GetSignatureHelp` (line 567) is the remaining unguarded site. |
| Approach | In `src/RoslynMcp.Host.Stdio/Tools/SymbolTools.cs`, insert `if (result is null) throw new KeyNotFoundException(SymbolLocatorFactory.FormatSymbolNotFoundMessage(locator));` immediately before line 567 (`return JsonSerializer.Serialize(result, JsonDefaults.Indented);`), mirroring the pattern at line 543 (`GetMemberHierarchy`) and line 592 (`GetSymbolRelationships`). No other files change — the fix is purely in the tool layer. Add a regression test method to `tests/RoslynMcp.Tests/Services/NavigationToolsNotFoundMessageTests.cs` following the `MemberHierarchy_MetadataNameOnly_NotFound_ThrowsKeyNotFound_NotBareNull` shape (line 160), asserting `KeyNotFoundException` for an unresolvable metadata-name locator passed to `GetSignatureHelp`. |
| Scope | Production files (1): `src/RoslynMcp.Host.Stdio/Tools/SymbolTools.cs`. Test files (1, extended): `tests/RoslynMcp.Tests/Services/NavigationToolsNotFoundMessageTests.cs`. Rule 3 exemption: tool-surface-only, 1 production file (within 2-file cap). |
| Tool policy | edit-only |
| Estimated context cost | 25000 |
| Risks | None significant. The fix is a one-liner mirroring an already-shipped pattern. Adjacent behavior: callers currently treating a bare `null` response as "no result" will receive a NotFound envelope instead — breaking for any caller that depended on the null, but that behavior was always a bug. Fanout probe skipped — surgical single-site edit, no cross-cutting symbol changes. |
| Validation | 1. Add test `SignatureHelp_MetadataNameOnly_NotFound_ThrowsKeyNotFound_NotBareNull` in `NavigationToolsNotFoundMessageTests` — asserts `KeyNotFoundException` for a garbage metadata name. 2. Confirm existing `NavigationToolsNotFoundMessageTests` suite passes. 3. `mcp__roslyn__compile_check` green after edit. |
| Performance review | N/A — correctness fix, no hot-path changes. |
| CHANGELOG category | Fixed |
| CHANGELOG entry (draft) | `symbol_signature_help` now returns a structured `{error, category: "NotFound", message}` envelope for unresolvable locators instead of a bare JSON `null`, matching its sibling tools. |
| Backlog sync | Close rows: [signature-help-bare-null]. |

### 2. elicitation-retry-exception-envelope

| Field | Content |
|---|---|
| Diagnosis | `TryRecoverMissingWorkspaceIdAsync` (`src/RoslynMcp.Host.Stdio/Middleware/StructuredCallToolFilter.cs:672–732`) has two unguarded `dispatchAsync` awaits that can throw: the `workspace_load` dispatch at line 711 and the retried tool dispatch at line 731. When either throws, the exception propagates out of `TryElicitAndRetryAsync`, which is itself awaited inside the outer `catch` block (line 226–238). A throw from within a `catch` handler is not caught by that same handler — the exception escapes the filter entirely, surfacing as an unhandled transport-level error rather than a structured `CallToolResult`. The elicitation failure path at line 688 correctly returns `null` (falls through to `BuildErrorResult`), but the downstream dispatch failures do not have equivalent guards. Root cause: missing try/catch wrappers around the two `dispatchAsync` calls in `TryRecoverMissingWorkspaceIdAsync`. |
| Approach | Wrap both `dispatchAsync` invocations inside `TryRecoverMissingWorkspaceIdAsync` in try/catch blocks: (1) the `workspace_load` dispatch at line 711, and (2) the retried original-tool dispatch at line 731. On catch, log a warning (mirroring the `elicitAsync` failure pattern at line 688) and return `null` — the caller's `null` check causes `TryElicitAndRetryAsync` to return `null`, and the outer catch falls through to `BuildErrorResult` with the structured error envelope. No change to the `elicitAsync` failure path (already correct). Single file edit: `src/RoslynMcp.Host.Stdio/Middleware/StructuredCallToolFilter.cs`. |
| Scope | Production files (1): `src/RoslynMcp.Host.Stdio/Middleware/StructuredCallToolFilter.cs`. Test files (1, extend existing): `tests/RoslynMcp.Tests/StructuredCallToolFilterElicitationTests.cs`. Rule 3 satisfied — 1 production file, no exemption required. |
| Tool policy | edit-only |
| Estimated context cost | 28000 |
| Risks | The `null`-return convention (meaning "fall through to schemaHint envelope") is consistent with the elicitation failure path at line 688 — no behavior change to the non-throwing paths. Adjacent: ensure `TryElicitAndRetryAsync` callers in the outer `catch` (line 226–238) do not need their own guard — they do not, because the fix is at the `TryRecoverMissingWorkspaceIdAsync` level before propagation. No bundle peers rejected. |
| Validation | New test in `StructuredCallToolFilterElicitationTests.cs`: inject a `dispatchAsync` stub that throws `InvalidOperationException` for the `workspace_load` call — assert `TryRecoverMissingWorkspaceIdAsync` returns `null` rather than throwing. Second test: stub where `workspace_load` succeeds but the retried tool dispatch throws — assert same. Existing tests must all stay green (`dotnet test --filter StructuredCallToolFilterElicitation`). |
| Performance review | N/A — correctness fix, no hot-path changes. |
| CHANGELOG category | Fixed |
| CHANGELOG entry (draft) | Fixed: exceptions thrown by `workspace_load` or the retried tool dispatch during elicitation-based `workspaceId` recovery now return the standard structured `CallToolResult` error envelope instead of escaping the filter as unhandled transport errors. |
| Backlog sync | Close rows: [elicitation-retry-exception-envelope]. |

### 3. workspace-id-omitted-residual-recovery-coherence

| Field | Content |
|---|---|
| Diagnosis | `IsWorkspaceIdRecoveryAllowedFor` at `src/RoslynMcp.Host.Stdio/Middleware/StructuredCallToolFilter.cs:457` gates on `schema is { Type: "string", Required: true }`. The 3 tools flipped to `Required:false` by the workspace-id-optional-readonly-surface-flip initiative (`go_to_definition`, `find_references`, `document_symbols`) therefore fail this gate — their exception-path elicitation recovery is silently dead. When a client omits `workspaceId` with 0 workspaces loaded and nothing discoverable (the `DiscoveryStatus.None` branch at line 666–668 returns `null`), the middleware falls through to `next()`, the binder does NOT throw a missing-required-arg error (the arg is optional), the call reaches the tool handler which then fails differently. Separately, the `TryAutoLoadWorkspaceAsync` doc-comment at line 604 says the `None` branch "falls through to the binder/elicitation path" — inaccurate for the 3 flipped tools, which never reach the elicitation gate. Two corrective choices exist: (A) decouple `IsWorkspaceIdRecoveryAllowedFor` from the `Required` flag (mirroring how `IsWorkspaceIdAutoResolveAllowedFor` at line 471–487 was already deliberately decoupled — its doc at line 465–469 explicitly calls this out); or (B) correct the stale comment only. Choice A is architecturally cleaner and preserves the stated UX intent. |
| Approach | **Chose option A** — decouple `IsWorkspaceIdRecoveryAllowedFor` from the `Required` flag to match the established `IsWorkspaceIdAutoResolveAllowedFor` pattern. In `src/RoslynMcp.Host.Stdio/Middleware/StructuredCallToolFilter.cs`: change line 457's predicate from `schema is { Type: "string", Required: true }` to `schema is { Type: "string" }` (dropping the `Required: true` constraint), making it consistent with `IsWorkspaceIdAutoResolveAllowedFor`. Update the XML doc on `IsWorkspaceIdRecoveryAllowedFor` to note the Required-independence (mirroring lines 465–469). Update the `TryAutoLoadWorkspaceAsync` `None`-branch comment at line 604 to remove the misleading "binder/elicitation path" parenthetical. In `tests/RoslynMcp.Tests/StructuredCallToolFilterElicitationTests.cs`: add a residual-case test asserting `IsWorkspaceIdRecoveryAllowedFor` returns `true` for a flipped (`Required:false`) tool (e.g. `go_to_definition` / `workspaceId`), pinning the chosen behavior. |
| Scope | Production files (1): `src/RoslynMcp.Host.Stdio/Middleware/StructuredCallToolFilter.cs`. Test files (1): `tests/RoslynMcp.Tests/StructuredCallToolFilterElicitationTests.cs`. Well within Rules 3 and 4. No Rule-3 exemption needed. |
| Tool policy | edit-only |
| Estimated context cost | 28000 |
| Risks | The `Required` constraint in `IsWorkspaceIdRecoveryAllowedFor` was intentional at time of writing (recovery only fires when the binder throws on a missing required arg). Removing it means the predicate returns `true` for optional tools — but the actual recovery path (`TryElicitMissingParameterAsync`) is only entered from the exception-catch block, and the binder never throws for a missing optional arg, so the gate relaxation is structurally safe; the predicate being `true` doesn't cause the code path to fire spuriously. Verify the elicitation test suite still passes in full after the change. Adjacent behavior: `IsElicitationAllowedFor` (line 308–313) calls `IsWorkspaceIdRecoveryAllowedFor` — the same reasoning applies; no concern. |
| Validation | 1. `IsWorkspaceIdRecoveryAllowedFor("go_to_definition", "workspaceId")` returns `true` (new test). 2. `IsWorkspaceIdRecoveryAllowedFor` returned `false` before the fix (confirm via test that would have been red). 3. Existing elicitation tests pass unchanged. 4. `mcp__roslyn__compile_check` clean on the single modified file. 5. `dotnet test --filter StructuredCallToolFilterElicitationTests` green. |
| Performance review | N/A — correctness fix, no hot-path changes. |
| CHANGELOG category | Fixed |
| CHANGELOG entry (draft) | Fixed: residual-case elicitation recovery (`IsWorkspaceIdRecoveryAllowedFor`) now fires for tools with `workspaceId` flipped to `Required:false` (e.g. `go_to_definition`, `find_references`, `document_symbols`), matching the design intent documented on `IsWorkspaceIdAutoResolveAllowedFor`; stale comment in `TryAutoLoadWorkspaceAsync` corrected. |
| Backlog sync | Close rows: [workspace-id-omitted-residual-recovery-coherence]. |

### 4. find-implementations-corlib-root-tighten-heuristic

| Field | Content |
|---|---|
| Diagnosis | `src/RoslynMcp.Roslyn/Services/ReferenceService.cs:424–435` — `IsCorlibAssembly` uses a broad `name.StartsWith("System.", StringComparison.Ordinal)` match as a catch-all after the three precise names (`System.Private.CoreLib`, `mscorlib`, `netstandard`, `System.Runtime`). This over-matches any third-party assembly whose name begins with `System.` (e.g. a custom `System.MyCompany.Foo`), classifying its metadata interface types as corlib implementation roots and suppressing the full enumeration path. The sibling guard `IsCorlibVirtualMember` (`ReferenceService.cs:374`) already uses the tighter `SpecialType: not SpecialType.None` gate for member-scoped corlib detection — `IsCorlibAssembly` has no equivalent precision. Not a silent-correctness regression (the `IsInSource` guard still fires for user-declared types), but imprecise: a non-corlib `System.*`-named assembly gets the hint incorrectly. |
| Approach | Modify `IsCorlibAssembly` in `src/RoslynMcp.Roslyn/Services/ReferenceService.cs` (private static method, lines 424–435). Replace the terminal `name.StartsWith("System.", StringComparison.Ordinal)` branch with an explicit allowlist of the well-known BCL surface assemblies plus a documented escape hatch: if the type has `SpecialType != SpecialType.None` that is a definitive corlib signal regardless of name. Mirror the SpecialType gate pattern from `IsCorlibVirtualMember` at line 374. Keeping the `StartsWith` net as a documented secondary fallback (gated/annotated) is acceptable per the acceptance criterion. Add a regression test asserting a `System.*`-named non-corlib metadata interface is NOT classified as a corlib implementation root. No other files need changing. |
| Scope | Production files (1): `src/RoslynMcp.Roslyn/Services/ReferenceService.cs`. Test files (1): `tests/RoslynMcp.Tests/` (extend or add a test class covering `IsCorlibImplementationRoot` / `IsCorlibAssembly`). Total within Rule 3 (≤ 4) and Rule 4 (≤ 3 test files). |
| Tool policy | edit-only |
| Estimated context cost | 28000 |
| Risks | (1) The allowlist approach trades over-matching for potential under-matching if a BCL assembly is absent from the list — mitigated by keeping `SpecialType`-based detection as the primary gate (Roslyn-authoritative) and the allowlist as a secondary net. (2) The `SpecialType` gate covers types the Roslyn enum knows about; less common BCL interfaces (e.g. `System.IAsyncDisposable`, `System.Buffers.IBufferWriter<T>`) may have `SpecialType.None` — executor should verify coverage before removing the name-based net entirely; keeping `StartsWith` as a documented secondary fallback is permitted. Fanout probe skipped — `IsCorlibAssembly` is private and called only from `IsCorlibImplementationRoot`. |
| Validation | (1) New regression test: construct a fake/mock `IAssemblySymbol` with name `System.MyCompany.Foo` (no special types) and assert `IsCorlibImplementationRoot` returns `false`. (2) Existing `find_implementations` tests continue to pass. (3) `mcp__roslyn__compile_check` green after edit. (4) Verify `IsCorlibVirtualMember` is NOT changed. (5) Manually verify `System.IDisposable`, `System.Collections.Generic.IEnumerable<T>`, `System.IComparable` still resolve as corlib roots (SpecialType-covered). |
| Performance review | N/A — correctness fix, no hot-path changes. `IsCorlibAssembly` is called once per `find_implementations` request on the resolved symbol's containing assembly; no loop, no allocation change. |
| CHANGELOG category | Fixed |
| CHANGELOG entry (draft) | Fixed `IsCorlibAssembly` heuristic in `find_implementations` to replace the broad `StartsWith("System.")` assembly-name match with a `SpecialType`-based primary gate and explicit BCL allowlist, preventing third-party `System.*`-named assemblies from being incorrectly classified as corlib implementation roots. |
| Backlog sync | Close rows: [find-implementations-corlib-root-tighten-heuristic]. |

### 5. workspace-validation-process-kill-failure-observability

| Field | Content |
|---|---|
| Diagnosis | `src/RoslynMcp.Roslyn/Services/WorkspaceValidationService.cs` lines 541 and 550 each contain `try { process.Kill(entireProcessTree: true); } catch { /* best-effort */ }` inside `CollectGitChangedFilesAsync`. Both catch blocks are empty — kill exceptions are silently discarded. `WorkspaceValidationService` has no `ILogger` field at all; the constructor accepts no logger parameter. If a `git status` process survives after a timeout or unexpected exception, the leaked process (and any associated file locks) is invisible: no warning is added to the validation envelope and no log line is emitted. The sibling `DotnetCommandRunner` established the canonical pattern: a `LoggerMessage.Define` static delegate + an `ILogger<T>` constructor parameter + a private `TryKillProcessTree` wrapper that logs `LogLevel.Warning` on kill failure. `WorkspaceValidationService` predates that pattern and was never updated. |
| Approach | 1. Add `ILogger<WorkspaceValidationService>? _logger` field. 2. Extend the public constructor to accept an optional `ILogger<WorkspaceValidationService>? logger = null` parameter and pass it through to the `internal` overload. 3. In the `internal` overload (line 52), accept the logger and store it. 4. Add a `private static readonly Action<ILogger, int, string, Exception?> LogProcessKillFailed` via `LoggerMessage.Define<int, string>` (parameters: processId, kill-context string) at class scope — mirror `DotnetCommandRunner.cs:32-36`. 5. Replace both empty `catch { /* best-effort */ }` blocks (lines 541 and 550) with a private `TryKillProcess` helper that calls `LogProcessKillFailed` when `_logger is not null`, identical to `DotnetCommandRunner.TryKillProcessTree`. No validation envelope shape changes. DI registration in `ServiceCollectionExtensions.cs` does NOT need updating because `ILogger<WorkspaceValidationService>` is resolved automatically once the constructor parameter is present. |
| Scope | Production files touched: 1 — `src/RoslynMcp.Roslyn/Services/WorkspaceValidationService.cs`. Test files modified: 1 — extend `tests/RoslynMcp.Tests/ValidateRecentGitChangesTests.cs` (or `WorkspaceValidationTimeoutTests.cs`) with a test that injects a controllable kill-fail seam via the `internal` constructor, asserts the warning log fires, and asserts the validation warning string is still emitted unchanged. |
| Tool policy | edit-only |
| Estimated context cost | 30000 |
| Risks | 1. `ILogger<WorkspaceValidationService>` is injected as optional (`= null`) to preserve backward compatibility with test callsites using the `internal` overload directly — confirm all test constructors are updated or pass `null`. 2. Adding a logger parameter shifts the parameter count of the `internal` constructor, so every test constructing it directly must pass the logger argument. Check all `WorkspaceValidation*` test files. 3. Fanout probe skipped — surgical edit, no cross-cutting symbol changes. |
| Validation | 1. `mcp__roslyn__compile_check` on `src/RoslynMcp.Roslyn/` after edit — zero errors. 2. New test: inject via the `internal` constructor with a fake `ILogger` sink, trigger the kill-throws path in `CollectGitChangedFilesAsync`, assert one `Warning`-level entry containing the process id, assert the returned `Warnings` still contains the original git-timeout/error warning string (envelope unchanged). 3. `dotnet test --filter WorkspaceValidation` — all existing tests pass. |
| Performance review | N/A — correctness fix, no hot-path changes. |
| CHANGELOG category | Fixed |
| CHANGELOG entry (draft) | Fixed swallowed `Process.Kill` cleanup failures in `WorkspaceValidationService`: kill exceptions are now logged at `Warning` level (with process id) when a logger is present, matching the pattern established by `DotnetCommandRunner`. Validation envelopes and warning strings are unchanged. |
| Backlog sync | Close rows: [workspace-validation-process-kill-failure-observability]. |

### 6. script-execution-supervisor-cancel-catch-observability

| Field | Content |
|---|---|
| Diagnosis | `src/RoslynMcp.Roslyn/Services/ScriptExecutionSupervisor.cs:271` contains `try { timeoutCts.Cancel(); } catch (ObjectDisposedException) { }` inside `AbandonWorkerOnHardDeadline`. The empty catch is intentional (the CTS may already be disposed if the worker finished before the hard deadline fired), but it is fully silent — no comment, no trace. A future maintainer cannot distinguish "approved race" from "swallowed error". |
| Approach | Add an inline comment inside the `catch (ObjectDisposedException)` body in `AbandonWorkerOnHardDeadline` explaining the race: the timeout CancellationTokenSource may already be disposed if the worker completed cleanly before the hard deadline; cancellation is therefore a no-op and the exception is expected. No log call is needed (`LogHardDeadlineCritical` already fires before this path; adding noise here is not useful). One file, one comment. |
| Scope | Production files: 1 (`src/RoslynMcp.Roslyn/Services/ScriptExecutionSupervisor.cs`). Test files: 0 (comment-only change; no behavior delta to assert). No exemption required. |
| Tool policy | edit-only |
| Estimated context cost | 15000 |
| Risks | None significant. Comment-only change; zero behavioral delta. Adjacent behavior (hard-deadline abandon semantics, `_abandonedEvaluations` counter) is untouched. |
| Validation | `mcp__roslyn__compile_check` after edit (comment-only, trivially clean). `./eng/verify-ai-docs.ps1` to confirm no doc-check regressions. No runtime test needed — no behavior change. |
| Performance review | N/A — correctness fix, no hot-path changes. |
| CHANGELOG category | Maintenance |
| CHANGELOG entry (draft) | Documented the approved `ObjectDisposedException` race in `ScriptExecutionSupervisor.AbandonWorkerOnHardDeadline` — the empty catch on `timeoutCts.Cancel()` at hard deadline is now annotated explaining the expected dispose-before-cancel scenario. |
| Backlog sync | Close rows: [script-execution-supervisor-cancel-catch-observability]. |

### 7. nuget-version-checker-test-wallclock-poll

| Field | Content |
|---|---|
| Diagnosis | Both wait helpers use a 10 s wall-clock poll with `Task.Delay(25)` rather than awaiting the background fetch directly. `NuGetVersionCheckerTests.cs:107-121` (`WaitForCompletionAsync`) and `ServerInfoUpdateLatestTests.cs:232-244` (`WaitForTerminalStatusAsync`) are structurally identical: spin-loop on `LastCheckStatus` until non-Pending/non-NeverChecked, fail on timeout. The underlying task is already stored in `NuGetVersionChecker._pendingCheck` (`NuGetVersionChecker.cs:66`, assigned at line 126), but it is private, so tests cannot await it. The production class has no other exposure path. |
| Approach | 1. In `src/RoslynMcp.Host.Stdio/Services/NuGetVersionChecker.cs`: add an `internal Task? PendingCheckForTest` property that returns `_pendingCheck` under `_lock`. This is the test seam; no behaviour change. Confirm `InternalsVisibleTo("RoslynMcp.Tests")` is already wired (do NOT re-add if present — duplicate attribute is a compile error). 2. In `tests/RoslynMcp.Tests/NuGetVersionCheckerTests.cs`: replace `WaitForCompletionAsync` (lines 107-121) with an await on `checker.PendingCheckForTest` with a 5 s `WaitAsync` guard, followed by a single `LastCheckStatus` assertion. 3. In `tests/RoslynMcp.Tests/ServerInfoUpdateLatestTests.cs`: replace `WaitForTerminalStatusAsync` (lines 232-244) with the same pattern. No handler stubs change; no production behaviour changes. |
| Scope | Production files (1): `src/RoslynMcp.Host.Stdio/Services/NuGetVersionChecker.cs`. Test files modified (2): `tests/RoslynMcp.Tests/NuGetVersionCheckerTests.cs`, `tests/RoslynMcp.Tests/ServerInfoUpdateLatestTests.cs`. No new test files. Within Rule 3 (1 production file) and Rule 4 (0 new, 2 extended). |
| Tool policy | edit-only |
| Estimated context cost | 25000 |
| Risks | Verify `InternalsVisibleTo` is already present before adding it — a duplicate attribute is a compile error. The `PendingCheckForTest` property must capture the task reference under `_lock` to avoid a torn read. The `ForceCacheExpired` reflection helper in both test files touches `_checkedAtUtc` directly — unaffected by the seam addition. The bounded HTTP-timeout test still relies on the checker's internal CTS elapsing; the await on `PendingCheckForTest` completes once `FetchLatestVersionAsync` catches `OperationCanceledException` and calls `RecordFailure` — no special handling needed. |
| Validation | 1. `dotnet test --filter "NuGetVersionCheckerTests"` — all existing tests pass with no `Assert.Fail` from the old poll path. 2. `dotnet test --filter "ServerInfoUpdateLatestTests"` — all tests pass. 3. `mcp__roslyn__compile_check` on `NuGetVersionChecker.cs` — zero errors. 4. Confirm no `Task.Delay(25)` remains in either test file after the edit. |
| Performance review | N/A — reliability fix; tests run faster (no 25 ms sleep loops). |
| CHANGELOG category | Maintenance |
| CHANGELOG entry (draft) | Replace wall-clock poll in NuGet version checker tests with a deterministic task-await seam, eliminating timing sensitivity under heavy CI parallel load. |
| Backlog sync | Close rows: [nuget-version-checker-test-wallclock-poll]. |

### 8. legacy-bug-id-tool-descriptions

| Field | Content |
|---|---|
| Diagnosis | Two shipped `[Description]` strings retain internal tracker ids that confuse external consumers and agents. `src/RoslynMcp.Host.Stdio/Tools/ValidationTools.cs` line 54 (`test_discover`): `"… large suites should be filtered with projectName and/or nameFilter (BUG-007). The response includes returnedCount/totalCount/hasMore…"` — `(BUG-007)` is a dead internal reference; the advice is otherwise correct. `src/RoslynMcp.Host.Stdio/Tools/MSBuildTools.cs` line 53 (`get_msbuild_properties`): `"… always pass a propertyNameFilter substring or an explicit includedNames allowlist unless you really need everything (BUG-008). The response includes totalCount/returnedCount/appliedFilter…"` — same pattern. Both descriptions were written during the bug-fix sprint and never scrubbed before publication. |
| Approach | Edit `src/RoslynMcp.Host.Stdio/Tools/ValidationTools.cs` line 54: remove `(BUG-007)` (or clean reword); retain the guidance about `projectName`/`nameFilter` and the pagination field names `returnedCount`/`totalCount`/`hasMore`. Edit `src/RoslynMcp.Host.Stdio/Tools/MSBuildTools.cs` line 53: remove `(BUG-008)`; retain the guidance about `propertyNameFilter`/`includedNames` and the response fields `totalCount`/`returnedCount`/`appliedFilter`. No functional change to tool behavior, parameters, or response shape. |
| Scope | Production files (2): `src/RoslynMcp.Host.Stdio/Tools/ValidationTools.cs`, `src/RoslynMcp.Host.Stdio/Tools/MSBuildTools.cs`. Test files (0): description-only change. Rule 3 exemption: tool-surface-only, 2 files (per addenda). |
| Tool policy | edit-only |
| Estimated context cost | 22000 |
| Risks | Low. Description strings are part of the MCP published surface — replacement text must remain accurate and actionable. Verify the rewritten sentences still name the correct field names (`returnedCount`/`totalCount`/`hasMore` for `test_discover`; `totalCount`/`returnedCount`/`appliedFilter` for `get_msbuild_properties`). No adjacent behavior at risk. |
| Validation | 1. `mcp__roslyn__compile_check` — zero new diagnostics after edit. 2. Grep for `BUG-007` and `BUG-008` across the repo — both return zero hits post-edit. 3. Optionally call `test_discover` and `get_msbuild_properties` via the MCP surface and confirm descriptions surface correctly. |
| Performance review | N/A — docs fix, no hot-path changes. |
| CHANGELOG category | Fixed |
| CHANGELOG entry (draft) | Fixed `test_discover` and `get_msbuild_properties` tool descriptions to remove internal bug tracker ids `BUG-007`/`BUG-008`; descriptions now reference only the shipped pagination and filter field names (`returnedCount`, `totalCount`, `hasMore`, `appliedFilter`). |
| Backlog sync | Close rows: [legacy-bug-id-tool-descriptions]. |

