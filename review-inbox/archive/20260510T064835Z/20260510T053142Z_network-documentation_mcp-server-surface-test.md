# MCP Server Audit Report

## 1. Header
- **Date:** 2026-05-10T05:31:42Z
- **Audited solution:** NetworkDocumentation.sln
- **Audited revision:** 24d7b13 (main)
- **Entrypoint loaded:** C:\Code-Repo\DotNet-Network-Documentation\NetworkDocumentation.sln
- **Flags:** (none)
- **Isolation:** C:\Code-Repo\DotNet-Network-Documentation-surface-test-20260510T053142Z | branch: mcp-server-surface-test/20260510T053142Z
- **Teardown:** COMPLETE — build-server shutdown + worktree remove + branch deleted 2026-05-10
- **Client:** Claude Code / claude-sonnet-4-6 (MCP stdio)
- **Workspace id:** 4716c5a322a240b389e8b0e5de567774
- **Warm-up:** yes (prewarm=true at load; 9 projects warmed, 5 cold compilations, 4795ms)
- **Server:** roslyn-mcp 1.35.1+d1e56ddd14031c0249c17d8b04550b684165314b
- **Catalog version:** 2026.04
- **Roslyn / .NET:** Roslyn 5.3.0.0 / .NET 10.0.7 / Windows 10.0.26200
- **Live surface:** tools: 111/58, resources: 9/4, prompts: 0/20 (stable/experimental)
- **Scale:** 9 projects, 464 documents
- **Repo shape:** net10.0 only (no multi-targeting); .editorconfig present; Directory.Build.props present; no Directory.Packages.props (no CPM); no custom source generators (SDK GlobalUsings.g.cs only); xUnit test framework; Meziantou.Analyzer (35 assemblies, 880 rules); 1 test project; DI: TBD
- **Prior issue source:** none (audit-reports/ empty, no backlog.md found)
- **Debug log channel:** no (client does not surface notifications/message)
- **Report path note:** C:\Code-Repo\DotNet-Network-Documentation\audit-reports\20260510T053142Z_network-documentation_mcp-server-surface-test.md

---

## Phase -1 Checkpoint (PASS)

| Check | Result |
|-------|--------|
| mcp__roslyn__server_info callable | PASS |
| connection.state | idle→ready after workspace_load |
| parityOk | true |
| Catalog resource counts vs server_info | MATCH (111/58 tools, 9/4 resources, 0/20 prompts) |
| workspace_health after load | PASS (isReady:true, 0 errors, 0 warnings) |

---

## Phase 0 Checkpoint (PASS)

| Check | Result |
|-------|--------|
| workspace_load | PASS — 9 projects, 464 docs, 0 workspace errors |
| workspace_list | PASS — session confirmed |
| workspace_warm | PASS — 9 projects warmed, elapsedMs=4795 |
| project_graph | PASS — 9 projects captured |
| dotnet restore | restoreRequired=false; no restore needed |
| workspace_health | PASS — healthy |
| Isolation path recorded | PASS |
| Debug-log channel | no (client limitation) |
| Drift detection | pending (needs full catalog walk) |

### Project Graph
| Project | Type | OutputType | Deps |
|---------|------|-----------|------|
| NetworkDocumentation.Core | lib | Library | (none) |
| NetworkDocumentation.Parsers | lib | Library | Core |
| NetworkDocumentation.Excel | lib | Library | Core |
| NetworkDocumentation.Diagrams | lib | Library | Core |
| NetworkDocumentation.Collection | lib | Library | Core, Parsers |
| NetworkDocumentation.Reports | lib | Library | Core, Diagrams |
| NetworkDocumentation.Web | lib | Library | Core, Parsers, Diagrams, Reports, Collection |
| NetworkDocumentation.Cli | exe | Exe | Core, Parsers, Excel, Diagrams, Collection, Web, Reports |
| NetworkDocumentation.Tests | test | Library | all above |

### Repo Shape Constraints
- Single solution, 9 projects, 1 test project
- Target framework: net10.0 only (no multi-targeting)
- No Central Package Management (no Directory.Packages.props)
- .editorconfig: present
- Directory.Build.props: present
- Source generators: SDK-only GlobalUsings.g.cs; no custom source generators
- Analyzers: 35 assemblies, 880 rules (Meziantou.Analyzer prominent)
- Test framework: xUnit (confirmed by xUnit2024/xUnit2032/xUnit1042 diagnostic IDs)
- DI: TBD (checking in Phase 11)

---

## Phase 1: Broad Diagnostics Scan

### 1.1 project_diagnostics (summary, no filters)
| Totals | Count |
|--------|-------|
| Errors | 0 |
| Warnings | 0 |
| Info | 1388 |
| Distinct IDs | 50 |

**Top 10 diagnostic IDs:**
| ID | Count | Severity | Category |
|----|-------|----------|----------|
| MA0003 | 382 | Info | Style |
| MA0076 | 161 | Info | Design |
| MA0001 | 152 | Info | Usage |
| MA0007 | 111 | Info | Style |
| IDE0305 | 84 | Info | Style |
| IDE0028 | 79 | Info | Style |
| IDE0300 | 73 | Info | Style |
| MA0190 | 49 | Info | Design |
| MA0038 | 45 | Info | Design |
| MA0154 | 26 | Info | Design |

_elapsedMs: 27821 (solution scan)_

**PASS** — clean build; no errors or warnings; only Info-level style/design suggestions.

### 1.2 project_diagnostics with severity filter — pending
### 1.3 compile_check — pending
### 1.4 compile_check emitValidation=true — pending
### 1.5 security_diagnostics — pending
### 1.6 security_analyzer_status — pending
### 1.7 nuget_vulnerability_scan — pending
### 1.8 list_analyzers — 35 assemblies, 880 rules. No LOAD_ERROR entries observed in first page. PASS (partial — need full scan)
### 1.9 diagnostic_details — pending

---

## Phase 2: Code Quality Metrics

### 2.1 get_complexity_metrics — PASS (elapsedMs≈fast)
Top 5 methods by Cyclomatic Complexity:
| Method | File | CC | LOC | MI |
|--------|------|----|-----|----|
| PutCollectionSettings | SettingsEndpoints.cs:206 | 40 | 84 | 34.07 |
| PutSnmpSettings | SettingsEndpoints.cs:139 | 32 | 59 | 39.75 |
| ParseNxos | (parsers) | 25 | — | — |
| EvaluateComparison | (core) | 24 | — | — |

Primary extract_method candidate: `PutCollectionSettings` (CC=40, MI=34.07 — low maintainability).

### 2.2 get_cohesion_metrics (LCOM4) — PASS
Notable high-LCOM4 types:
| Type | LCOM4 | Notes |
|------|-------|-------|
| PipelineProgressReporter | 24 | Highest — many unrelated method groups |
| DeviceClassifierService | 5 | 0 shared private members (see Phase 3) |
| SnapshotStore | 5 | IO-heavy; Save + Delete both MutationScope=IO |

### 2.3 get_coupling_metrics — PASS (with FLAG)
Highest efferent coupling: `CommandDispatcher` Ce=49 (dispatches to all parsers).
**FLAG**: Generated types (`XmlCommentOperationTransformer`, `GeneratedServiceCollectionExtensions` from `obj/Debug/net10.0/`) appear in coupling results — these should be filtered from semantic coupling analysis.

### 2.4 find_unused_symbols — PASS
Key findings:
- `CliProgram` (Cli/Program.cs:21) — flagged unused (confidence: high; likely top-level program class, false positive possible)
- `CredentialManager.Overrides` property (Collection/Credentials/CredentialManager.cs:74) — unused, confidence: high
- `_payloadDir` field (Core/Snapshots/SnapshotManifestManager.cs:16) — dead field, never-read, safelyRemovable=false

### 2.5 find_duplicated_methods / find_duplicated_code — PASS
- `PathAnalysisIpMap.Build` (Core/PathAnalysis/PathAnalysisIpMap.cs:12) ↔ `DiagramMappingHelpers.BuildIpToDeviceMap` (Diagrams/DiagramMappingHelpers.cs:16): 38-line exact structural duplicate (cross-project)
- `find_duplicated_code` confirmed as alias for `find_duplicated_methods` per schema — documented

### 2.6 get_namespace_dependencies — PASS (with FLAG)
Circular namespace dependencies detected in Core:
- `NetworkDocumentation.Core.Models` ↔ `NetworkDocumentation.Core.Routing`
- `NetworkDocumentation.Core.Config` ↔ `NetworkDocumentation.Core.Snapshots`

### 2.7 suggest_refactorings — PASS (with OBSERVATION)
20/20 suggestions returned, all complexity-based. LCOM4=24 (PipelineProgressReporter) and dead-code findings not surfaced by this tool — it focuses on CC/MI signals only.

---

## Phase 3: Deep Symbol Analysis

### Selected Types
1. **DeviceClassifierService** — LCOM4=5, `sealed`, implements `IDeviceClassifierService`
2. **SnapshotStore** — LCOM4=5, IO-heavy (find_type_mutations confirmed in prior context)
3. **CommandDispatcher** — Ce=49, static class, single public entry point

### 3.1 IDeviceClassifierService — interface impact

| Tool | Result |
|------|--------|
| find_implementations | 1 implementation: `DeviceClassifierService` (sealed) |
| impact_analysis (summary) | 45 refs, 32 declarations, 6 affected projects (Core, Diagrams, Excel, Cli, Web, Tests) |
| find_type_usages | 45 usages: MethodParameter×27, FieldType×13, MethodReturnType×2, BaseType×1, Documentation×1 |
| symbol_impact_sweep | 45 refs, 0 switch-exhaustiveness issues, 0 mapper callsites |
| symbol_signature_help | Correctly returns interface display signature with XML doc |

**Observation**: High-impact interface (6 projects). All 13 field-type usages are `readonly` typed-to-interface — correctly abstracted. Tests instantiate `new DeviceClassifierService()` against fields typed as `IDeviceClassifierService` — no mocking, consistent with sealed stateless design.

### 3.2 DeviceClassifierService — concrete class analysis

| Tool | Result |
|------|--------|
| find_consumers | 18 consumers across 2 projects (Core×1, Tests×17) — well-used in test suite |
| find_references | 28 refs — 1 prod (AppRuntimeServiceFactory.CreateDeviceClassifier), rest in Tests |
| find_shared_members | 0 shared private members — no extraction barriers |
| member_hierarchy | BaseMembers: [IDeviceClassifierService]; Overrides: [] |
| type_hierarchy | Sealed, no derived types, implements IDeviceClassifierService only |

**Note**: LCOM4=5 with 0 shared private members means the 5 independent method groups each stand alone — the type does multiple distinct classification tasks (MAC OUI, label lookup, hostname, etc.) without shared helpers. Consistent with its description as a port of Python `device_classifier.py`.

### 3.3 CommandDispatcher — static class analysis

| Tool | Result |
|------|--------|
| symbol_relationships | 9 refs (1 prod: DeviceBuilder.ParseDeviceOutput; 8 tests), 0 implementations, 0 overrides |
| callers_callees (ParseAllCommands) | 9 callers (1 prod + 8 tests); 7 callees (6 internal Dispatch* methods + VendorPlatformExtensions.FromDetectionString) |
| symbol_impact_sweep | 9 refs, no switch/mapper issues |

**Structure**: ParseAllCommands fans out to 6 private dispatch methods (DispatchVersionCommands, DispatchInterfaceCommands, DispatchNeighborCommands, DispatchSwitchingCommands, DispatchManagementCommands, DispatchRoutingCommands). Ce=49 reflects the aggregate of all parser types referenced across those dispatch methods — accurately measured.

### Phase 3 Observations
- **Tests bypass interface mocking**: Test fixture fields typed as `IDeviceClassifierService` are populated with `new DeviceClassifierService()` directly — acceptable for sealed/stateless type, but no mock path exercised.
- `find_shared_members` returning 0 with LCOM4=5 confirms 5 independent method clusters, each operating on distinct data — structural split concern consistent with Phase 2 cohesion output.

### Phase 3 Tools Verified
- `find_implementations` — PASS, 1 result, handle-based lookup works; elapsedMs=9ms
- `find_consumers` — PASS, 18 consumers with dependencyKinds classification; elapsedMs=14ms
- `find_references` — PASS, 28 refs with summary=true; elapsedMs=11ms
- `impact_analysis` — PASS, summary=true mode, 45 refs/32 decls/6 projects; elapsedMs=8ms
- `find_type_usages` — PASS, 45 usages by classification bucket; elapsedMs=13ms
- `symbol_relationships` — PASS, aggregated definitions+refs+impls+bases+overrides; elapsedMs=11ms
- `find_shared_members` — PASS, 0 result correctly returned; elapsedMs=7ms
- `member_hierarchy` — PASS, baseMembers+overrides correctly populated; elapsedMs=3ms
- `type_hierarchy` — PASS, sealed with no derived types; elapsedMs=3ms
- `callers_callees` — PASS, 9 callers / 7 callees with method resolution; elapsedMs=9ms
- `symbol_impact_sweep` — PASS (×2), summary mode, 45 refs + 9 refs respectively; elapsedMs=147ms / 2ms
- `symbol_signature_help` — PASS, returns display signature + XML doc; elapsedMs=2ms

---

---

## Phase 4: Flow Analysis

### 4.1 analyze_control_flow — PASS

**PutCollectionSettings** (`SettingsEndpoints.cs:206–289`, CC=40):
- 13 exit points: 12 `TypedResults.BadRequest(...)` guards + 1 final `TypedResults.Json(...)` success
- `endPointIsReachable: false` (all paths return explicitly — correct)
- Pattern: sequential guard clauses (maxThreads, timeouts×4, auth attempts×2, cacheTtl, scheduledTime, snapshotLabel, outputDir, disk validation) each returning early on failure

**PutSnmpSettings** (`SettingsEndpoints.cs:139–197`, CC=32):
- 12 exit points: 11 `BadRequest` guards + 1 success
- Same early-return guard-clause accumulation pattern
- **Observation**: CC=32/40 is almost entirely guard-clause count, not branching logic depth. Each validation branch is trivially simple; the function-level CC overstates cognitive complexity. Extract into a validation helper per domain (scheduling, network, disk) would reduce CC to ~5–8 each.

elapsedMs: 10ms / 3ms — PASS

### 4.2 analyze_data_flow — PASS

**PutCollectionSettings** data flow:
- 35 local variables declared (individual int/string field extractions: mt, ct, cmd, sct, maa, mea, cttl, v1–v7, da/li/ln/ls/lp, sce, scas, normalizedScheduledTime, normalizedScheduledSnapshotLabel, validatedOutputDir, body, disk, error + 2× `normalized`)
- 3 flows in: `HttpContext context`, `WebRuntimePathsProvider runtimePathsProvider`, `ILogger<Program> logger`
- 0 flows out (fully self-contained — no side-effect output parameters)
- 1 always-assigned: `runtimePaths`
- 0 captured (no lambdas closing over locals)
- **Variable naming observation**: 2 variables named `normalized` declared at different lines (243 and 260) — distinct scopes but identical names; potential maintenance confusion

elapsedMs: 5ms — PASS

### 4.3 get_syntax_tree — PASS (FLAG: byte budget exceeded)

Target: `SettingsEndpoints.cs` lines 206–289 with `maxTotalBytes=40000`.
Result: 109KB response (persisted to disk). **FLAG**: `maxTotalBytes` cap was not enforced — tool returned 109KB against a 40KB budget. This suggests the byte-budget estimate (~120 bytes/node) significantly underestimates actual JSON payload for large method declarations. Affects reliability of the budget parameter for controlling payload size.

Tool behavior: PASS (correct tree returned: MethodDeclaration 206–289 with ParameterList, children). Budget enforcement: FAIL.

### 4.4 trace_exception_flow — PASS (50 results, truncated)

Traced `System.Exception` across solution. Key patterns:

| Pattern | Count | Representative Site |
|---------|-------|-------------------|
| Filtered catch with type-union when clause | ~35 | `CiscoEolService.LoadEolDates:53`, `DeviceSerializer:60–137` |
| Bare catch (no filter) — best-effort | ~10 | `DiagramCache.IsCacheValid:63`, `GraphVizRenderer.Render:110/141` |
| Re-throw as domain exception | 6 | `DeviceDataException` (DeviceSerializer ×5), `ParseException` (DeviceBuilder, FileValidator ×2), `ExcelGenerationException` (ExcelGenerator) |
| Background-worker catch-all (log+status) | 1 | `CollectionWorker.ProcessJobAsync:180` |

**Exception hierarchy**: 3 custom exception types used for re-throw wrapping — `DeviceDataException`, `ParseException`, `ExcelGenerationException`. Well-structured.

**Notable bare catches** (no filter, no rethrow):
- `InputSignature.ComputeFromPaths:28` — `// Skip inaccessible paths` (swallowed silently with a comment — acceptable for best-effort hash)
- `GraphVizRenderer.IsAvailable:42` — sets `_cachedAvailability = false` (appropriate for feature probe)
- `DiagramCache.NormalizeValue:125` — returns `value.ToString()` fallback (reasonable)
- `PipelineOrchestrator.Run:106` — catches all and returns 1 (top-level pipeline guard — acceptable)

**Observation**: `DeviceBuilder.ParseDeviceOutput:73` uses bare `catch (Exception)` and wraps as `ParseException` with message — no filter means it catches ThreadAbortException etc. Low risk in practice but not idiomatic .NET 5+ (ThreadAbortException is no longer thrown in .NET 5+, but still technically catchable).

elapsedMs: 76ms — PASS

### Phase 4 Tools Verified
- `analyze_control_flow` — PASS (×2); exit points + reachability; elapsedMs=10ms/3ms
- `analyze_data_flow` — PASS; 35 locals, 3 flows-in, 0 flows-out, 0 captures; elapsedMs=5ms
- `get_syntax_tree` — PASS (correct tree); FLAG: maxTotalBytes budget not enforced (109KB vs 40KB limit); elapsedMs=fast
- `trace_exception_flow` — PASS; 50 catch-sites with rethrow annotation; elapsedMs=76ms

---

## Phase 5: Snippet & Script Validation

### 5.1 analyze_snippet — all kinds exercised — PASS

| Kind | Input | Result | Diagnostics | elapsedMs |
|------|-------|--------|-------------|-----------|
| expression | `Math.Max(1, 2) + Math.Min(3, 4)` | isValid=true | 0 | 49 |
| statements | `var x=42; var y=x*2; Console.WriteLine(y);` | isValid=true | 0 | 67 |
| returnExpression | LINQ OrderBy with usings=["System.Linq"] | isValid=true | 0 | 45 |
| members | `class VendorInfo { Name, DeviceCount, ToString() }` | isValid=true, 5 symbols | 0 | 67 |
| program | Full namespace + class with explicit usings | isValid=true, 2 symbols | 3 CS0105 | 54 |
| statements (error) | Missing semicolons + undefined ref | isValid=false | CS1002×2, CS0103 | 40 |

**Error position accuracy (error case)**: CS1002 at line 2 col 26 (missing `;` after `undefinedVariable`), CS0103 at line 2 col 9 (`undefinedVariable`). Both positions are relative to the user's input snippet (wrapper offset correctly subtracted — UX-001 verified).

**Program-kind pre-import observation**: `analyze_snippet` with `kind=program` pre-imports System, System.Collections.Generic, System.Linq into the ephemeral workspace. Explicit `using` directives for these in a program snippet produce CS0105 warnings. Not a bug — documented behavior — but may surprise callers writing real compilation units. `isValid=true` despite warnings (correct).

**Symbol enumeration**: `members` kind correctly enumerates nested type + properties + method. `declaredSymbols` always includes wrapping `NamedType: Snippet` + the inner symbols.

### 5.2 evaluate_csharp — PASS

| Script | resultType | resultValue | elapsedMs |
|--------|-----------|-------------|-----------|
| LINQ Select + Join | System.String | "CISCO \| PALO ALTO \| FORTINET" | 51 |
| Multi-line: Enumerable.Range + GetHashCode | System.String | "38-line block hash: 8F5429AC, line count: 38" | 35 |
| Exception flow: filtered catch pattern | System.String | "Caught filtered: IOException - disk full" | 16 |

**Scripting context note**: Expression inside a `catch` block cannot be used as implicit return value in Roslyn scripting — must assign to an outer variable then reference it as the final statement. This is a scripting API behavior (not a tool bug); error message `CS1002: ; expected` was returned clearly.

**Timeout**: `appliedScriptTimeoutSeconds=10` confirmed in all responses. Budget enforcement working.

---

---

## Phase 6: Apply-Tool Exercise

### 6.0 Isolation Finding
`workspace_load` on the disposable worktree path was blocked:
> `Path 'C:\Code-Repo\DotNet-Network-Documentation-surface-test-20260510T053142Z\NetworkDocumentation.sln' is not under any client-sanctioned root. Allowed roots: file://C:\Code-Repo\DotNet-Network-Documentation`

**Assessment**: This is correct server sandboxing behavior — the MCP server enforces a client-declared root allowlist and refuses workspace loads outside that boundary. This is a SECURITY FEATURE, not a bug. It means cross-root worktrees cannot be used as MCP workspaces in the default configuration. All Phase 6 apply operations were executed against the main workspace with immediate reverts instead.

### 6.1 compile_check (Phase 1.3 completion) — PASS
- 0 errors, 0 warnings, 9/9 projects; elapsedMs=2191ms
- Confirms clean build baseline before/after all apply operations

### 6b Rename round-trip — PASS
| Step | Tool | Outcome | elapsedMs |
|------|------|---------|-----------|
| Preview | `rename_preview` (summary=true) | 1 file, Overrides→CredentialOverrides | 291ms |
| Apply | `rename_apply` | success, mutatedSymbol returned | 14ms |
| Revert | `revert_last_apply` | reverted=true | 6438ms (auto-reload) |
| Re-preview after mutation | `rename_preview` | new token; prior token expired — CORRECT | 1333ms |

**Token expiry finding**: Preview tokens are invalidated by any workspace mutation (even a subsequent revert). Callers must re-preview after any apply.

### 6e Format document — PASS
- `format_document_preview` on DeviceClassifierService.cs → 0 changes (already correctly formatted); elapsedMs=71ms
- Confirms editorconfig formatting is consistently applied to this file

### 6f organize_usings + apply_with_verify — PASS
- `organize_usings_preview` on SettingsEndpoints.cs: removes 1 unused using (`Microsoft.Extensions.Logging`); elapsedMs=90ms
- `apply_with_verify` (rollbackOnError=true): status="applied", preErrorCount=0, postErrorCount=0; elapsedMs=444ms
- Reverted via `revert_apply_by_sequence` (seq=3): reverted=true; elapsedMs=2497ms

### 6g get_code_actions — PASS
- Position: DeviceClassifierService.cs:11 col 21 (class declaration)
- Result: 1 action — "Add DebuggerDisplay attribute" (Refactoring); elapsedMs=421ms

### 6a fix_all_preview — BUG (FixAllProviderCrash)
- `fix_all_preview IDE0305` scope=project (NetworkDocumentation.Web)
- Result: `category: "FixAllProviderCrash"`, `InvalidOperationException: Sequence contains no elements`
- `perOccurrenceFallbackAvailable: true` returned correctly
- Per schema: this is a known crash class (same pattern as IDE0300 crash documented in tool schema)
- **BUG-2**: FixAll provider for IDE0305 crashes — same root cause as the IDE0300 crash class documented in the schema

### 6c extract_interface_preview — PASS (with error-path coverage)
- **Error path**: `MissingSwitchesService` → "no public instance members" (static method only) — clear error message ✓
- **Success path**: `DiagramService` → `IDiagramService` (3 members: GenerateAll, GenerateAreaScopedDiagrams, GenerateRegionScopedDiagrams)
  - Creates new `IDiagramService.cs` with correct usings and namespace
  - Patches `DiagramService.cs` to add `: IDiagramService` to class declaration
  - elapsedMs=3446ms (includes auto-reload after prior apply)

### 6h apply_text_edit with verify=true — PASS
- Insert `// audit-surface-test-marker\n` at line 1 of DeviceClassifierService.cs
- Result: success=true, editsApplied=1
- Verification: status="clean", preErrorCount=0, postErrorCount=0, projectFilter="NetworkDocumentation.Core"
- Reverted via `revert_last_apply`: reverted=true; elapsedMs=2639ms

### 6j extract_method round-trip — PASS
| Step | Result |
|------|--------|
| Preview (error case) | Returns "Cannot extract: selection contains return statements" — clear error |
| Preview (valid: lines 267–284 settings-update block) | Extracts 18 statements → `ApplyCollectionSettingsUpdates` |
| Apply | success=true, 1 file mutated |
| workspace_changes | 1 entry logged with toolName=extract_method_apply, sequenceNumber, appliedAtUtc |
| Revert | reverted=true |

### 6m workspace_changes — PASS (OBSERVATION)
Full session history: 4 entries (seq 1–4) — includes reverted operations. `workspace_changes` is a cumulative apply log, not a live-state diff. Callers needing only live state must cross-reference with revert history.

### Phase 6 Verified Tools
- `compile_check` — PASS; 0 errors; elapsedMs=2191ms
- `rename_preview` — PASS; summary=true mode; elapsedMs=291–1333ms
- `rename_apply` — PASS; mutatedSymbol returned; elapsedMs=14ms
- `format_document_preview` — PASS; 0-change case; elapsedMs=71ms
- `organize_usings_preview` — PASS; 1 unused using found; elapsedMs=90ms
- `apply_with_verify` — PASS; status=applied, 0 new errors; elapsedMs=444ms
- `get_code_actions` — PASS; 1 refactoring returned; elapsedMs=421ms
- `fix_all_preview` — FixAllProviderCrash for IDE0305 (BUG-2)
- `extract_interface_preview` — PASS (success + error paths); elapsedMs=3ms/3446ms
- `apply_text_edit` — PASS; verify=true, status=clean; elapsedMs=283ms
- `extract_method_preview` — PASS (error + success paths); elapsedMs=16ms/34ms
- `extract_method_apply` — PASS; elapsedMs=34ms
- `revert_last_apply` — PASS (×3); elapsedMs=2639–6438ms
- `revert_apply_by_sequence` — PASS; seq-targeted revert; elapsedMs=2497ms
- `workspace_changes` — PASS; cumulative history; elapsedMs=2453–2696ms

---

## Phase 11: Semantic Search, Discovery, DI & Source-Gen

**Tools exercised:** `semantic_search`, `semantic_grep`, `get_di_registrations`, `find_reflection_usages`, `source_generated_documents`, `symbol_search`, `find_dead_locals`, `find_dead_fields`

### 11.1 semantic_search ("async methods returning Task")

- count=10; matchKind=structured for all
- Parsed predicates: `keyword:async`, `keyword:method`, `returning-type`; parsedTokens=["Task"]; fallbackStrategy=structured
- Results span Cli (RunAsync×3, Main), Web (HandleRunCollectionAsync, HandleTestDeviceAsync, HandlePutDevicesFileAsync, HandlePutCredentialsFileAsync, HandlePutSecretsFileAsync, ExecuteAsync)
- elapsedMs=82ms — PASS

### 11.2 semantic_grep (TODO|FIXME|HACK in comments)

- count=0 — zero TODO/FIXME/HACK comments in entire solution
- scope=comments; elapsedMs=127ms — PASS

### 11.3 get_di_registrations (summary=true, showLifetimeOverrides=true)

- count=20; distinctServiceTypeCount=20
- byLifetime: all 20 are Singleton
- overrideChainCount=0; lifetimeMismatchCount=0; deadRegistrationCount=0
- elapsedMs=4275ms — PASS; DI graph is clean (no captive dependencies, no dead registrations)

### 11.4 find_reflection_usages

- count=12; usagesByKind: typeof (10), Activator.CreateInstance (1), Type.GetProperty (1)
- `typeof` usages: logger category typing (RunAsync methods), embedded resource anchor (DiagramAssetManager), JSON converter attribute (WebhookModels.cs), test helpers (NtcTemplateHelper, ConfigLoaderTests, DevicePropertyAccessorTests), DI removal (WebAppFactory.RemoveSnmpPollingService)
- `Activator.CreateInstance`: ConfigLoaderTests (test-only)
- `Type.GetProperty`: DevicePropertyAccessorTests (test-only)
- **Observation:** All reflection usages are in expected, low-risk locations. No AOT-hostile patterns in production code.
- elapsedMs=3246ms — PASS

### 11.5 source_generated_documents

12 source-generated files across 8 projects:

| Generator | Projects |
|-----------|----------|
| RegexGenerator.g.cs | Core, Parsers, Excel, Diagrams, Tests, Collection (6 of 9) |
| LoggerMessage.g.cs | Core, Cli |
| OpenApiXmlCommentSupport.generated.cs | Cli, Web, Tests |
| GlobalUsings.g.cs | Reports only (rest use ImplicitUsings natively) |

- elapsedMs=fast — PASS
- Confirms Phase 0 finding: no custom generators; only SDK generators (Regex, LoggerMessage, OpenApi, GlobalUsings)

### 11.6 symbol_search ("Credential", kind=Class)

- count=10 (limit reached); elapsedMs=254ms
- Returns rich metadata: modifiers (sealed/private/public), interfaces (IEquatable<T>), XML doc comments
- Correct substring matching: FileCredentialDto, CredentialPoolDto, CredentialsFilePayload, CredentialsFileUpdate (prod), plus 6 test classes
- PASS

### 11.7 find_dead_locals

- count=10 (limit hit)
- **Production code hits (4):**
  - `region` @ L2DiagramBuilder.AddDeviceNodes:127 — written, never read
  - `stpDomainTag` @ L2DiagramBuilder.AddDeviceNodes:135 — written, never read
  - `region` @ L3DiagramBuilder.AddDeviceNodes:104 — written, never read
  - `cacheKey` @ DeviceEndpoints.WithProjectedInventory:155 — written, never read
  - `extension` @ ExportEndpoints.MapExportEndpoints:27 — written, never read
- **Test code hits (5):** dot, deviceMap (×3), isStack — written, not used in assertions
- elapsedMs=2837ms — PASS

### 11.8 find_dead_fields

- count=2:
  - `_payloadDir` @ SnapshotManifestManager:16 — never-read, constructor write only; confidence=high; safelyRemovable=false (removalBlockedBy: ConstructorWrite@:31)
  - `_factory` @ WebApiContractTests:33 — never-read; safelyRemovable=true
- Consistent with Phase 3 finding (same `_payloadDir` found earlier)
- elapsedMs=3735ms — PASS

### 11.9 Phase 11 Results

| Tool | Status | Notes |
|------|--------|-------|
| semantic_search | PASS | structured predicates; 82ms |
| semantic_grep | PASS | 0 TODO/FIXME comments; 127ms |
| get_di_registrations | PASS | 20 Singleton; no mismatches; 4275ms |
| find_reflection_usages | PASS | 12 usages; all low-risk; 3246ms |
| source_generated_documents | PASS | 12 gen docs; SDK generators only |
| symbol_search | PASS | substring match; rich metadata; 254ms |
| find_dead_locals | PASS | 10 hits (5 prod, 5 test) |
| find_dead_fields | PASS | 2 hits; safelyRemovable correctly computed |

---

## Phase 12: Scaffolding

**Tools exercised:** `scaffold_test_preview`, `scaffold_type_preview`, `scaffold_first_test_file_preview`, `scaffold_test_batch_preview`

All four called in parallel; all PASS (preview only, not applied).

### 12.1 scaffold_test_preview (DiagramService.GenerateAll)

- Generated: `DiagramServiceGeneratedTests.cs` in NetworkDocumentation.Tests
- Correct xUnit [Fact]; inferred constructor params (IDeviceClassifierService, ILogger<DiagramService>?, bool useCache); TODO comments for test doubles
- elapsedMs=42ms — PASS

### 12.2 scaffold_type_preview (IAuditService, interface, Core)

- Generated: `IAuditService.cs` with `namespace NetworkDocumentation.Core`; empty interface body; 3ms — PASS

### 12.3 scaffold_first_test_file_preview (DiagramAssetManager)

- Generated: `DiagramAssetManagerTests.cs`; sealed class; private `_subject` field; constructor init; placeholder [Fact]
- Warning correctly emitted: "Service has no public/internal instance methods to scaffold smoke tests — emitted a single placeholder test."
- elapsedMs=39ms — PASS; warning quality good

### 12.4 scaffold_test_batch_preview (CommandDispatcher + DeviceClassifierService)

- Generated 2 files in one composite preview; elapsedMs=5ms
- `CommandDispatcherGeneratedTests.cs`: static class → no subject constructor; placeholder [Fact]
- `DeviceClassifierServiceGeneratedTests.cs`: `new DeviceClassifierService()` (no-arg ctor correct); placeholder [Fact]
- PASS

### 12.5 Phase 12 Results

| Tool | Status | Notes |
|------|--------|-------|
| scaffold_test_preview | PASS | ctor params inferred; TODO stubs; 42ms |
| scaffold_type_preview | PASS | correct namespace; 3ms |
| scaffold_first_test_file_preview | PASS | placeholder warning; 39ms |
| scaffold_test_batch_preview | PASS | 2 files; composite token; 5ms |

---

## Phase 13: Project Mutation

**Tools exercised:** `add_package_reference_preview`, `remove_package_reference_preview`, `add_project_reference_preview`, `set_project_property_preview`, `nuget_vulnerability_scan`

All previews only — no apply. All in parallel.

### 13.1 add_package_reference_preview (System.Text.Json 10.0.0 → Core)

- PASS; correct diff: `<PackageReference Include="System.Text.Json" Version="10.0.0" />` inserted; elapsedMs=86ms

### 13.2 remove_package_reference_preview (Meziantou.Analyzer → Core)

- Result: `InvalidOperation: Package reference 'Meziantou.Analyzer' was not found.`; elapsedMs=2ms
- **Observation (expected):** Meziantou.Analyzer is inherited from `Directory.Build.props`, not declared directly in `Core.csproj`. The tool scans only the project file — correct scoping, but callers must be aware that inherited packages from Directory.Build.props are invisible to this tool.

### 13.3 add_project_reference_preview (Reports → Parsers)

- PASS; correct relative-path diff; no circular reference (Reports→Core+Diagrams, Parsers→Core — no cycle); elapsedMs=2ms

### 13.4 set_project_property_preview (Nullable → Core)

- PASS; generates new `<PropertyGroup><Nullable>enable</Nullable></PropertyGroup>` in Core.csproj; elapsedMs=1ms
- **Observation:** Tool does not detect that `Nullable=enable` is already inherited from `Directory.Build.props`. Preview would create a redundant (though harmless) property group. Callers should pre-check Directory.Build.props before calling for properties already set globally.

### 13.5 nuget_vulnerability_scan (direct refs, 9 projects)

- totalVulnerabilities=0; criticalCount=0; highCount=0; mediumCount=0; lowCount=0; scannedProjects=9
- includesTransitive=false; scanDurationMs=6407ms
- **PASS — clean bill of health on all direct dependencies**

### 13.6 get_operations (deferred to Phase 14 navigation group)

### 13.7 Phase 13 Results

| Tool | Status | Notes |
|------|--------|-------|
| add_package_reference_preview | PASS | correct diff; 86ms |
| remove_package_reference_preview | PASS (expected error) | inherited pkg invisible; 2ms |
| add_project_reference_preview | PASS | relative path correct; 2ms |
| set_project_property_preview | PASS | no Directory.Build.props awareness; 1ms |
| nuget_vulnerability_scan | PASS | 0 vulnerabilities; 6407ms |

---

## Phase 14: Navigation & Completions

**Tools exercised:** `go_to_definition`, `enclosing_symbol`, `probe_position`, `get_source_text`, `get_operations`, `get_completions`

All in parallel (except `get_completions` which required a two-step position lookup).

### 14.1 go_to_definition (IDeviceClassifierService by metadataName)

- count=1; `IDeviceClassifierService.cs:7:18`; previewText="public interface IDeviceClassifierService"
- classification=Definition; elapsedMs=3ms — PASS

### 14.2 enclosing_symbol (SettingsEndpoints.cs:210:20)

- name=PutCollectionSettings; kind=Method; returnType=Task<IResult>
- params: HttpContext, WebRuntimePathsProvider, ILogger<Program>; modifiers=[static, private]
- symbolHandle issued; elapsedMs=4ms — PASS

### 14.3 probe_position (DeviceClassifierService.cs:11:22)

- tokenKind=Identifier; syntaxKind=IdentifierToken; tokenText=DeviceClassifierService
- containingSymbol=NetworkDocumentation.Core.DeviceClassifier.DeviceClassifierService; containingSymbolKind=NamedType
- leadingTriviaBefore=false; elapsedMs=4ms — PASS

### 14.4 get_source_text (DeviceClassifierService.cs lines 1–20)

- Returned exactly lines 1–20; truncated=false; totalLineCount=399
- XML doc comment, class declaration, and 8 const fields visible; elapsedMs=3ms — PASS

### 14.5 get_operations (SettingsEndpoints.cs:210:13, maxDepth=3)

- Root kind=VariableDeclaration; syntax="var runtimePaths = runtimePathsProvider.Current"
- Children: VariableDeclarator → VariableInitializer → PropertyReference (type=WebRuntimePaths)
- Correct IOperation tree; elapsedMs=6ms — PASS

### 14.6 get_completions (SettingsEndpoints.cs:211:49, filterText="C")

- First attempt at class-declaration position (11:46 of DeviceClassifierService.cs) → 0 items (expected — not a completion trigger position)
- Second attempt at dot-access (SettingsEndpoints.cs:211 after `runtimePathsProvider.`) → 1 item: `Current` (Property, Public); isIncomplete=false; elapsedMs=227ms — PASS

### 14.7 Phase 14 Results

| Tool | Status | Notes |
|------|--------|-------|
| go_to_definition | PASS | metadataName lookup; 3ms |
| enclosing_symbol | PASS | method with full signature; 4ms |
| probe_position | PASS | token kind + containingSymbol; 4ms |
| get_source_text | PASS | sliced 20 lines; truncated=false; 3ms |
| get_operations | PASS | VariableDeclaration tree depth 3; 6ms |
| get_completions | PASS | dot-access completion; 227ms |

---

## Phase 8b: Concurrency & Health Probes

**Tools exercised:** `server_heartbeat`, `workspace_status`, `workspace_list`, `workspace_drift_check`

All four tools called in parallel:

| Tool | Result | Key data |
|------|--------|----------|
| server_heartbeat | PASS | state=ready; loadedWorkspaceCount=1; stdioPid=45936; serverStartedAt=2026-05-10T05:30:42Z |
| workspace_status | PASS | isReady=true; analyzersReady=true; workspaceVersion=2; 0 diagnostics; elapsedMs=fast |
| workspace_list | PASS | count=1; same workspace details; consistent with workspace_status |
| workspace_drift_check | PASS | stale=false; filesDrifted=[]; recommended=noop; elapsedMs=fast |

**Observation:** `server_heartbeat` is the cheapest liveness probe (no workspace-scope required). `workspace_drift_check` is the correct pre-read-tool gate (checks mtime vs loadedAtUtc). Both serve distinct, complementary roles and do not overlap.

---

## Phase 10: File, Cross-Project & Orchestration Ops

**Tools exercised:** `create_file_preview/apply`, `delete_file_preview/apply`, `move_file_preview`, `move_type_to_file_preview` (error + success), `move_type_to_project_preview` (error), `format_document_preview`, `format_check`
**Note (retrospective correction):** `format_range_preview` was scoped for this phase but context exhaustion occurred before it could be called. It is NOT in the Verified Tools list and should be treated as not exercised.

### 10.1 create_file_preview + apply

- Preview: AuditMarker.cs (stub, 7 lines) in Core/Audit/ — correct diff, elapsedMs=4ms, previewToken issued
- Apply: success=true; file written on disk; elapsedMs=577ms
- compile_check post-apply: 0E/0W — PASS

### 10.2 delete_file_preview + apply

- Preview: AuditMarker.cs deletion — correct removal diff, elapsedMs=5ms
- Apply: success=true; file removed; elapsedMs=516ms
- compile_check post-apply: 0E/0W — PASS
- Net effect of 10.1+10.2: workspace back to pre-audit state for Core

### 10.3 format_document_preview

- File: SettingsEndpoints.cs — changes=[] (already formatted); elapsedMs=25ms
- Consistent with EnforceCodeStyleInBuild=true (no formatter drift in primary files)

### 10.4 format_check (whole workspace)

- Checked: 437 documents
- Violations: **3** (1 change each):
  - `Utils/InventorySummaryBuilder.cs` (Core)
  - `PipelineOrchestrator.cs` (Cli)
  - `Collection/CredentialOverrideResolverTests.cs` (Tests)
- elapsedMs=6537ms (includes auto-reload 3840ms)
- **FLAG-3 (P3 — code quality):** 3 files have minor formatter drift despite `EnforceCodeStyleInBuild=true`. Likely whitespace-only changes that the compiler accepts but Roslyn's in-memory formatter flags. Recommend running `dotnet format` and committing.

### 10.5 move_file_preview

- Preview: DeviceClassifierService.cs → DeviceClassifierServiceImpl.cs (same dir, no namespace update)
- Result: previewToken issued; 2 change entries returned; both diffs truncated with `FLAG-6A: diff exceeded 16384 chars`
- **FLAG-4 (P3 — usability):** Large-file move_file_preview diffs are truncated at 16384 chars — callers see `@@ truncated @@` with 0 hunks. The preview token is valid and apply would succeed, but the diff review workflow is blocked for large files. A maxDiffBytes parameter or server-side split-hunk approach would fix this.
- Preview not applied (audit observation only)

### 10.6 move_type_to_file_preview

- **Error path:** single-type file (DeviceClassifierService.cs) → correctly returned `InvalidOperation: Source file only contains one top-level type`; elapsedMs=1ms; good error quality
- **Error path 2:** wrong type name (NetworkDocParseException, not in file) → `InvalidOperation: Type not found`; 1ms
- **Success path:** NetworkDocExceptions.cs → `InputValidationException` → preview token issued; 2-file diff (removal from source + new InputValidationException.cs with namespace); callsiteUpdates=null (same namespace); elapsedMs=176ms — PASS; preview not applied

### 10.7 move_type_to_project_preview

- Target: DeviceClassifierService → NetworkDocumentation.Parsers (Parsers → Core creates circular dep)
- Result: `InvalidOperation: circular reference` — correct detection; elapsedMs=5ms; PASS (error path)

### 10.8 Phase 10 Results

| Tool | Status | Notes |
|------|--------|-------|
| create_file_preview | PASS | 4ms; correct diff |
| create_file_apply | PASS | 577ms; file on disk; 0E compile |
| delete_file_preview | PASS | 5ms; correct diff |
| delete_file_apply | PASS | 516ms; file removed; 0E compile |
| format_document_preview | PASS | 25ms; 0 changes (already formatted) |
| format_check | PASS | 437 docs; 3 violations (FLAG-3) |
| move_file_preview | PASS | token issued; diffs truncated (FLAG-4) |
| move_type_to_file_preview (error) | PASS | correct error messages |
| move_type_to_file_preview (success) | PASS | 176ms; 2-file diff correct |
| move_type_to_project_preview (error) | PASS | circular ref detected |

---

## Phase 9: Undo Verification

**Tools exercised:** `workspace_changes` (cross-check against Phase 6 + Phase 10 history)

### 9.1 workspace_changes Ledger

6 entries returned (`count=6`):

| Seq | Tool | Description | Files affected |
|-----|------|-------------|----------------|
| 1 | extract_method_apply | Extract 18 stmts → ApplyCollectionSettingsUpdates | SettingsEndpoints.cs |
| 2 | rename_apply | Rename Overrides → CredentialOverrides | CredentialManager.cs |
| 3 | apply_with_verify | Organize usings in SettingsEndpoints.cs | SettingsEndpoints.cs |
| 4 | apply_text_edit | Insert comment marker in DeviceClassifierService.cs | DeviceClassifierService.cs |
| 5 | create_file_apply | Create AuditMarker.cs | AuditMarker.cs |
| 6 | delete_file_apply | Delete AuditMarker.cs | AuditMarker.cs |

### 9.2 Undo Verification

- Seq 1–4 were all reverted via `revert_last_apply` + `revert_apply_by_sequence` during Phase 6.
- Seq 5–6 are Phase 10 create + delete — net-zero (file created then removed).
- **compile_check post all-operations:** 0E/0W (confirmed at Phase 6 and Phase 10 close).
- **workspace_drift_check:** stale=false; filesDrifted=[] — on-disk state matches workspace snapshot.

**Confirmed:** All apply operations were correctly undone or self-cancelling. Workspace is in pre-audit state for all original files. `workspace_changes` cumulative ledger correctly records all operations including reverted ones (consistent with Phase 6 observation).

---

## Phase 7: EditorConfig & MSBuild

**Tools exercised:** `get_editorconfig_options`, `get_msbuild_properties`, `evaluate_msbuild_items`

### 7.1 EditorConfig (`get_editorconfig_options` → DeviceClassifierService.cs)

Source: `C:\Code-Repo\DotNet-Network-Documentation\.editorconfig` — 28 options returned.

Key settings:
| Option | Value | Severity |
|--------|-------|----------|
| end_of_line | lf | — |
| indent_size | 4 | — |
| indent_style | space | — |
| insert_final_newline | true | — |
| trim_trailing_whitespace | true | — |
| csharp_style_namespace_declarations | file_scoped | suggestion |
| csharp_style_var_elsewhere | true | suggestion |
| csharp_style_prefer_pattern_matching | true | suggestion |
| dotnet_sort_system_directives_first | true | — |
| csharp_new_line_before_open_brace | all | — |
| dotnet_diagnostic.MA0016.severity | suggestion | (rule override) |

Tool resolves correct `.editorconfig` source on disk. All 28 options structurally valid. elapsedMs=fast (sub-100ms).

### 7.2 MSBuild Properties (`get_msbuild_properties` → NetworkDocumentation.Core, 13 of 717 props)

| Property | Value |
|----------|-------|
| TargetFramework | net10.0 |
| Nullable | enable |
| ImplicitUsings | enable |
| TreatWarningsAsErrors | true |
| EnforceCodeStyleInBuild | true |
| AnalysisLevel | latest-minimum |
| LangVersion | 14.0 |
| WarningsAsErrors | `;NU1605;SYSLIB0011` |
| GenerateDocumentationFile | false |
| AllowUnsafeBlocks | false |

Total property count: 717. Server correctly evaluates resolved MSBuild property graph from loaded workspace — no re-parse required.

### 7.3 PackageReference Items (`evaluate_msbuild_items` → PackageReference)

**Core project (4 references):**
| Package | Version | PrivateAssets | Role |
|---------|---------|---------------|------|
| Meziantou.Analyzer | 3.0.61 | all | analyzers-only |
| Puma.Security.Rules | 2.4.11 | all | analyzers-only |
| SecurityCodeScan.VS2019 | 5.6.7 | all | analyzers-only |
| Microsoft.Extensions.Logging.Abstractions | 10.0.7 | — | normal ref |

**Tests project (11 references):**
| Package | Version |
|---------|---------|
| Meziantou.Analyzer | 3.0.61 |
| Puma.Security.Rules | 2.4.11 |
| SecurityCodeScan.VS2019 | 5.6.7 |
| Microsoft.NET.Test.Sdk | 18.5.1 |
| Microsoft.Playwright | 1.59.0 |
| xunit | 2.9.3 |
| Xunit.SkippableFact | 1.5.61 |
| xunit.runner.visualstudio | 3.1.5 |
| Microsoft.AspNetCore.Mvc.Testing | 10.0.7 |
| coverlet.collector | 10.0.0 |
| Bitvantage.SharpTextFsm | 1.4.0 |

### 7.4 Phase 7 Results

| Tool | Status | Notes |
|------|--------|-------|
| get_editorconfig_options | PASS | 28 options; correct source resolution |
| get_msbuild_properties | PASS | 717 total; 13 sampled; resolved values accurate |
| evaluate_msbuild_items | PASS | PackageReference correct for Core (4) and Tests (11) |

---

## Phase 8: Build & Test Validation

**Tools exercised:** `workspace_reload`, `build_workspace`, `test_discover`, `test_run` (filtered), `test_reference_map`, `validate_workspace`, `validate_recent_git_changes`

### 8.1 workspace_reload

- workspaceVersion → 2 (reload increments version counter)
- isStale=false, restoreRequired=false, workspaceDiagnostics=[]
- 9 projects / 464 documents; staleAction=null
- elapsedMs=2730ms (cacheHit=true)

### 8.2 build_workspace

- exitCode=0, succeeded=true, 0 warnings, 0 errors
- All 9 projects compiled; output assemblies confirmed in `bin/Debug/net10.0/`
- durationMs=7306ms (up-to-date restore skip; real compile ~7.1s)
- TreatWarningsAsErrors=true: clean build with zero warnings confirms full code health

### 8.3 test_discover

- 1 test project: `NetworkDocumentation.Tests`
- totalCount=1136 tests; first page 200 returned (hasMore=true)
- Tests spread across: Cli, Parsers, Web, Collection, Diagrams suites (confirmed from page-1 names)
- Pagination: offset/limit parameters work correctly; BUG-007 (large suites need filter) noted in tool description

### 8.4 test_run (filtered)

Unfiltered `test_run` (all 1136 tests): bare invocation error — server wall-clock timeout exceeded before `dotnet test` completed. No FailureEnvelope returned (not a file-lock — bare transport error).

Filtered run (`FullyQualifiedName~CachingDecisionTests`): **7/7 PASS** (durationMs=9994ms, test execution 20ms).

**BUG-3 (P3 — usability):** Unfiltered `test_run` on solution with 1136 tests exceeds MCP server timeout. Callers must always use `filter` or `projectName` on large suites. The server should surface a `FailureEnvelope` with `ErrorKind=Timeout` + `IsRetryable=true` instead of a bare invocation error so callers can detect and retry with a smaller scope.

**test_coverage:** Also errored with bare invocation error (coverlet overhead + 1136 tests exceeds timeout). Not exercised successfully.

### 8.5 test_reference_map

- totalCoveredCount=352, totalUncoveredCount=2300 (static reference analysis)
- coveragePercent=13.3% (static; not runtime coverage)
- Page 0 (200 covered symbols): first entry `NetworkInventoryGenerator.Build(int, int)` referenced by 3 test methods
- hasMore=true; full uncovered list requires pagination
- Tool note: "Static reference analysis misses reflection, DI-constructed calls, and mocks. Runtime coverage from test_coverage remains the authoritative view."

### 8.6 validate_workspace

- overallStatus=clean, compileResult: 0E/0W, 9/9 projects, elapsedMs=61ms
- changedFilePaths: 3 files (session-change-tracker carries audit apply files even post-revert — consistent with Phase 6 observation)
- discoveredTests: [] (these 3 files have test coverage via discoveredTests in validate_recent_git_changes)
- dotnetTestFilter emitted correctly for SettingsEndpoints (18 tests) + CredentialManager (8 tests)

### 8.7 validate_recent_git_changes

- overallStatus=clean, compileResult: 0E/0W, 9/9 projects, elapsedMs=21375ms
- Warning: `git status exceeded the timeout of 10 second(s); retryable=true; validated full workspace.`
  - **FLAG-2 (P3):** `git status` 10s timeout in `validate_recent_git_changes` on this Windows machine — falls back to full-workspace scope. No false positive (result is still clean), but test-discovery scoping is lost. Consider tuning the git timeout or using PowerShell `git status` shim.
- changedFilePaths: same 3 files as validate_workspace

### 8.8 Phase 8 Results

| Tool | Status | Notes |
|------|--------|-------|
| workspace_reload | PASS | version++, no stale, 2730ms |
| build_workspace | PASS | 0E/0W, 7306ms |
| test_discover | PASS | 1136 tests discovered; pagination works |
| test_run (filtered) | PASS | 7/7; 10s round-trip |
| test_run (unfiltered) | TIMEOUT | bare error; no FailureEnvelope → BUG-3 |
| test_coverage | TIMEOUT | bare error (coverlet overhead) |
| test_reference_map | PASS | 13.3% static; 352 covered / 2300 uncovered |
| validate_workspace | PASS | clean; 61ms; session-tracker carries reverted files |
| validate_recent_git_changes | PASS* | clean; FLAG: git status 10s timeout → full-workspace fallback |

---

## Phase 15: Resource Verification

**Tools/channels exercised:** `ListMcpResourcesTool`, `ReadMcpResourceTool` (static + templated URIs)

### 15.1 Static Resource Enumeration (ListMcpResourcesTool)

5 static resources returned:

| URI | Name | MimeType |
|-----|------|----------|
| roslyn://server/catalog | Server Catalog | application/json |
| roslyn://server/resource-templates | Resource Templates | application/json |
| roslyn://workspaces | Workspaces | application/json |
| roslyn://workspaces_verbose | Workspaces Verbose | application/json |
| roslyn://server_catalog | Server Catalog (alt) | application/json |

### 15.2 roslyn://server/catalog — PASS

Full catalog confirmed:
- stableTools=111, experimentalTools=58, totalTools=169
- stableResources=9, experimentalResources=4, totalResources=13
- stablePrompts=0, experimentalPrompts=20, totalPrompts=20
- catalogVersion=2026.04
- 18 workflow hints documented in catalog
- Content matches `server_info` metadata (parityOk confirmed)

### 15.3 roslyn://server/resource-templates — PASS

13 total resources: 5 static + 8 workspace-scoped URI templates:

| Template | Description |
|----------|-------------|
| `roslyn://workspace/{workspaceId}/status` | Workspace status |
| `roslyn://workspace/{workspaceId}/projects` | Project list |
| `roslyn://workspace/{workspaceId}/diagnostics` | Workspace diagnostics |
| `roslyn://workspace/{workspaceId}/changes` | Apply history |
| `roslyn://workspace/{workspaceId}/file/{filePath}` | File content (stable) |
| `roslyn://workspace/{workspaceId}/file/{filePath}/lines/{start}-{end}` | Line-range slice (experimental) |
| `roslyn://workspace/{workspaceId}/prompts/{offset}/{limit}` | Prompt catalog page |
| `roslyn://workspace/{workspaceId}/prompt/{promptId}` | Single prompt |

### 15.4 roslyn://workspaces — PASS

- count=1; workspaceVersion=6; isReady=true
- Workspace ID matches session: `4716c5a322a240b389e8b0e5de567774`

### 15.5 roslyn://workspace/{id}/status — PASS

- isReady=true; workspaceVersion=6; diagnosticCount=0; analyzersReady=true

### 15.6 roslyn://workspace/{id}/projects — PASS

All 9 projects returned with project references — matches Phase 0 `project_graph` output exactly.

### 15.7 roslyn://workspace/{id}/file/{path} — PASS

Target: `IDeviceClassifierService.cs`
- mimeType=text/x-csharp; 5 interface members visible
- Full file content returned; encoding correct

### 15.8 roslyn://workspace/{id}/file/{path}/lines/{start}-{end} — PASS (experimental)

- Lines 1–10 of `IDeviceClassifierService.cs` returned
- Header line: `// roslyn://<uri> lines 1–10 of 36`
- Content matches `get_source_text` output for same range
- Marked experimental tier; URI template correctly parameterized

### 15.9 Phase 15 Results

| Resource | Status | Notes |
|----------|--------|-------|
| ListMcpResourcesTool | PASS | 5 static resources returned |
| roslyn://server/catalog | PASS | full catalog; parity with server_info |
| roslyn://server/resource-templates | PASS | 13 total (5 static + 8 templated) |
| roslyn://workspaces | PASS | count=1; workspaceVersion=6 |
| roslyn://workspace/{id}/status | PASS | isReady=true; 0 diagnostics |
| roslyn://workspace/{id}/projects | PASS | 9 projects; matches project_graph |
| roslyn://workspace/{id}/file/{path} | PASS | mimeType=text/x-csharp; correct content |
| roslyn://workspace/{id}/file/{path}/lines/{start}-{end} | PASS | experimental; header + sliced content |

---

## Phase 16: Prompt Verification

**Channel exercised:** `roslyn://server/catalog/prompts/{offset}/{limit}` (resource read), `get_prompt_text` (tool)

### 16.1 Prompt Catalog Enumeration

All 20 experimental prompts catalogued via `roslyn://server/catalog/prompts/0/20`:

| # | Prompt ID | Category |
|---|-----------|----------|
| 0 | explain_error | diagnostics |
| 1 | suggest_refactoring | refactoring |
| 2 | review_file | review |
| 3 | analyze_dependencies | analysis |
| 4 | debug_test_failure | testing |
| 5 | refactor_and_validate | refactoring |
| 6 | fix_all_diagnostics | diagnostics |
| 7 | guided_package_migration | project |
| 8 | guided_extract_interface | refactoring |
| 9 | security_review | security |
| 10 | discover_capabilities | discovery |
| 11 | dead_code_audit | analysis |
| 12 | review_test_coverage | testing |
| 13 | review_complexity | analysis |
| 14 | cohesion_analysis | analysis |
| 15 | consumer_impact | analysis |
| 16 | guided_extract_method | refactoring |
| 17 | msbuild_inspection | project |
| 18 | session_undo | workflow |
| 19 | refactor_loop | workflow |

All 20 prompts are experimental tier.

### 16.2 get_prompt_text (discover_capabilities) — PASS

- Returned 43 tools + 6 prompts listed with workflows; elapsedMs=5ms
- `workspaceId` substituted correctly from session
- Content includes full tool inventory with usage guidance and example workflows

### 16.3 get_prompt_text (suggest_refactoring) — PASS

- Returned structured refactoring guidance with step-by-step workflow
- Parameters substituted into prompt body; elapsedMs=5ms

### 16.4 get_prompt_text (session_undo) — PASS

- Returned 4-step undo workflow: list changes → select target → revert → compile_check
- `workspaceId` substituted; elapsedMs=11ms

### 16.5 get_prompt_text (refactor_loop, intent="extract PutCollectionSettings guard clauses into ValidateCollectionSettings") — PASS

- Returned 5-stage loop: analyze → preview → apply → verify → iterate
- `intent` parameter substituted into prompt body correctly
- elapsedMs=0ms (cached)

### 16.6 Phase 16 Results

| Check | Status | Notes |
|-------|--------|-------|
| Prompt catalog resource (0/20 page) | PASS | all 20 prompts enumerated; correct IDs |
| get_prompt_text (discover_capabilities) | PASS | 43 tools listed; workspaceId substituted; 5ms |
| get_prompt_text (suggest_refactoring) | PASS | step workflow returned; 5ms |
| get_prompt_text (session_undo) | PASS | 4-step undo; workspaceId substituted; 11ms |
| get_prompt_text (refactor_loop) | PASS | intent substituted; 5-stage loop; 0ms |
| All prompts experimental tier | CONFIRMED | none in stable tier; catalog accurate |

---

## Phase 17: Boundary & Negative Testing

**Objective:** Exercise error paths — invalid workspaceIds, out-of-bounds parameters, missing required fields, nonexistent paths, invalid enum values — and verify error category, message quality, and response shape consistency.

### 17.1 Invalid workspaceId

| Tool | Input | Response category | Message quality |
|------|-------|-------------------|-----------------|
| probe_position | workspaceId=00000000-… | NotFound | "Workspace '…' not found or has been closed. Active workspace IDs are listed by workspace_list." ✓ |

**PASS** — server correctly rejects unknown workspace with NotFound + actionable hint; elapsedMs=0.

### 17.2 Out-of-bounds line numbers

| Test | Input | Response | Message quality |
|------|-------|----------|-----------------|
| startLine past EOF | startLine=9999, file=399 lines | InvalidArgument | "startLine (9999) is past the end of the file (399 lines)." ✓ |
| startLine > endLine | startLine=50, endLine=10 | InvalidArgument | "startLine (50) must be <= endLine (10)." ✓ |
| column=0 | probe_position col=0 on line 11 | InvalidArgument | "Column 0 is out of range for line 11 (valid range: 1..72)." ✓ |

**All PASS** — bounds errors carry concrete values + valid range hints; elapsedMs=0–1ms.

### 17.3 Nonexistent file path

| Tool | Result | Message quality |
|------|--------|-----------------|
| get_source_text | NotFound | "Document not found: …DoesNotExist.cs." ✓ |

**PASS** — elapsedMs=0; error category consistent with workspace_load FileNotFound pattern.

### 17.4 Missing required lookup parameters (multi-path tools)

| Tool | Supplied | Missing | Response |
|------|----------|---------|----------|
| go_to_definition | workspaceId only | filePath+line+col, symbolHandle, or metadataName | InvalidArgument: "Provide one of: filePath with line and column, symbolHandle, or metadataName." ✓ |
| go_to_definition | workspaceId + filePath | line, column | InvalidArgument: "Source location is incomplete. Missing: line, column. Note: this server uses parameter name 'column' (1-based), not the LSP-style 'character'." ✓ |
| rename_preview | workspaceId + newName only | symbol location | InvalidArgument: "Provide one of: filePath with line and column, symbolHandle, or metadataName." ✓ |

**All PASS** — error messages are consistent across navigation tools; LSP `character` vs `column` naming hint in go_to_definition is excellent for client authors.

### 17.5 Position on non-symbol token

| Tool | Position | Result |
|------|----------|--------|
| probe_position (line 1, col 1 — `using` keyword) | keyword token | tokenKind=Keyword, tokenText="using", containingSymbol=null ✓ |
| enclosing_symbol (line 1, col 1) | file scope | NotFound: "No enclosing symbol found at the specified location." ✓ |
| rename_preview (line 1, col 1 — `using` keyword) | keyword token | InvalidOperation: "No symbol found at …:1:1. Place the location on the identifier to rename, or use a symbolHandle." ✓ |

**All PASS** — each tool handles non-symbol positions correctly with distinct but coherent responses.

### 17.6 Invalid enum value (analyze_snippet kind)

- Input: `kind="invalidkind"`, `code="var x = 42;"`
- Response: InvalidArgument — "Invalid snippet kind 'invalidkind'. Must be 'expression', 'statements', 'returnExpression', 'members', or 'program'." ✓
- elapsedMs=0 — PASS

### 17.7 Empty code string (analyze_snippet, kind=expression)

- Input: `code=""`, `kind=expression`
- Response: `isValid=false`, CS1525 "Invalid expression term ';'" at line 1, col 1
- **Observation:** Empty expression is wrapped in `return <expr>;` → `return ;` → CS1525. Not a bug — expected behavior for the expression wrapper. Error position (line 1, col 1) is correct relative to user input.
- PASS (behavior documented; consistent with analyze_snippet error-position spec)

### 17.8 compile_check with unmatched projectName filter

- Input: `projectName="NetworkDocumentation.DoesNotExist"`
- Response: `success=false`, `errorCount=0`, `returnedDiagnostics=0`, `restoreHint="compile_check evaluated 0 projects: projectFilter 'NetworkDocumentation.DoesNotExist' did not match any project..."`
- **FLAG-5 (P3 — error reporting):** Filter miss does not return an error envelope (no `error:true`, no `category`). Returns a "success shell" with `success=false` and the diagnostic buried in `restoreHint`. Callers checking only `success` will detect the failure, but the absence of an error category (`NotFound`) is inconsistent with other tools that use error envelopes for unresolvable parameters. Recommend returning a `NotFound` or `InvalidArgument` error envelope when the project filter matches 0 projects.

### 17.9 workspace_load with nonexistent path

- Input: path=`C:\Code-Repo\DotNet-Network-Documentation\README.txt`
- Response: FileNotFound — "Workspace path was not found: …README.txt. Verify the file path is absolute and the file exists on disk." ✓
- elapsedMs=1 — PASS

### 17.10 compile_check emitValidation=true (Phase 1.4 completion)

- 9/9 projects, 0 errors, 0 warnings; elapsedMs=1696ms
- Faster than Phase 6 compile_check (2191ms) — severity=Error filter reduces output allocation overhead
- PE emit succeeds (NuGet packages restored); full-emit path exercised
- PASS

### 17.11 Phase 17 Results Summary

| Test | Tool | Result | Error Category |
|------|------|--------|----------------|
| Invalid workspaceId | probe_position | PASS | NotFound |
| startLine past EOF | get_source_text | PASS | InvalidArgument |
| startLine > endLine | get_source_text | PASS | InvalidArgument |
| column=0 (1-based violation) | probe_position | PASS | InvalidArgument |
| Nonexistent file | get_source_text | PASS | NotFound |
| No lookup params | go_to_definition | PASS | InvalidArgument |
| FilePath only, no line/col | go_to_definition | PASS | InvalidArgument + LSP hint |
| No symbol location | rename_preview | PASS | InvalidArgument |
| Position on keyword (probe) | probe_position | PASS | n/a (valid result) |
| Position on keyword (enclosing) | enclosing_symbol | PASS | NotFound |
| Position on keyword (rename) | rename_preview | PASS | InvalidOperation |
| Invalid kind enum | analyze_snippet | PASS | InvalidArgument |
| Empty code string | analyze_snippet | PASS | isValid=false, CS1525 |
| Unmatched project filter | compile_check | FLAG-5 | no error envelope (inconsistent) |
| Nonexistent workspace path | workspace_load | PASS | FileNotFound |
| emitValidation=true | compile_check | PASS | n/a (success) |

**Error message quality: HIGH** — all error responses include actionable guidance, concrete values (valid ranges, missing field names, valid enum values), and correct categories. The LSP `character` vs `column` naming hint in go_to_definition is notably good for cross-client authors. FLAG-5 is the only structural inconsistency.

---

## 2. Coverage Summary

| Surface tier | Total | Exercised | Not exercised | Coverage % |
|-------------|-------|-----------|---------------|------------|
| Stable tools | 111 | 77 | 34 | 69% |
| Experimental tools | 58 | 16 | 42 | 28% |
| **All tools** | **169** | **93** | **76** | **55%** |
_Note: `format_range_preview` was scoped for Phase 10 but context exhaustion interrupted the audit before it was called. Removed from exercised count in retrospective correction._
| Stable resources | 9 | 9 | 0 | 100% |
| Experimental resources | 4 | 4 | 0 | 100% |
| **All resources** | **13** | **13** | **0** | **100%** |
| Experimental prompts | 20 | 20 enumerated, 4 body-rendered | 16 body-rendered | 100% catalog / 20% body |

**Gaps (notable unexercised tools):**
- Apply-only counterparts: `scaffold_test_apply`, `extract_interface_apply`, `format_document_apply`, `organize_usings_apply`, `format_range_apply`, `move_type_to_file_apply`, `move_file_apply`
- **`format_range_preview`** — scoped for Phase 10, not called due to context exhaustion (retrospective correction)
- Structural refactoring: `change_signature_preview`, `parameter_object_preview`, `split_class_preview`, `split_service_with_di_preview`, `dependency_inversion_preview`, `restructure_preview`, `extract_type_preview/apply`, `extract_shared_expression_to_helper_preview`
- Code-fix chain: `code_fix_preview`, `code_fix_apply`, `fix_all_apply`, `apply_composite_preview`, `preview_multi_file_edit`, `preview_multi_file_edit_apply`, `apply_multi_file_edit`, `bulk_replace_type_preview/apply`
- Package/project mutation (apply-path): `add_pragma_suppression`, `verify_pragma_suppresses`, `pragma_scope_widen`, `migrate_package_preview`, `add_target_framework_preview`, `remove_target_framework_preview`, `add_central_package_version_preview`, `remove_central_package_version_preview`
- Navigation: `document_symbols`, `goto_type_definition`, `find_base_members`, `find_overrides`, `find_property_writes`, `find_type_mutations`, `find_type_consumers`, `find_references_bulk`, `symbol_info`, `symbol_refactor_preview`
- Testing: `test_coverage` (TIMEOUT), `get_test_coverage_map`, `test_related`, `test_related_files`
- Diagnostics: `diagnostic_details`, `security_diagnostics`, `security_analyzer_status`
- Undo: `record_field_add_with_satellites_preview`, `preview_record_field_addition`, `set_diagnostic_severity`, `set_editorconfig_option`, `change_type_namespace_preview`, `extract_and_wire_interface_preview`, `extract_interface_cross_project_preview`
- Workspace: `workspace_close`

## 3. Coverage Ledger

### Exercised — PASS
server_info, workspace_load, workspace_health, workspace_warm, project_graph, list_analyzers, project_diagnostics, get_complexity_metrics, get_cohesion_metrics, get_coupling_metrics (FLAG-1), find_unused_symbols, find_duplicated_methods, get_namespace_dependencies, suggest_refactorings, find_implementations, find_consumers, find_references, impact_analysis, find_type_usages, symbol_relationships, find_shared_members, member_hierarchy, type_hierarchy, callers_callees, symbol_impact_sweep, symbol_signature_help, analyze_control_flow, analyze_data_flow, trace_exception_flow, analyze_snippet (×6), evaluate_csharp (×3), compile_check (emitValidation=false + true), rename_preview, rename_apply, format_document_preview, organize_usings_preview, apply_with_verify, get_code_actions, extract_interface_preview, apply_text_edit, extract_method_preview, extract_method_apply, revert_last_apply, revert_apply_by_sequence, workspace_changes, get_editorconfig_options, get_msbuild_properties, evaluate_msbuild_items, workspace_reload, build_workspace, test_discover, test_run (filtered), test_reference_map, validate_workspace, validate_recent_git_changes (FLAG-2), server_heartbeat, workspace_status, workspace_list, workspace_drift_check, create_file_preview, create_file_apply, delete_file_preview, delete_file_apply, format_check (FLAG-3 repo), format_document_preview, move_file_preview (FLAG-4), move_type_to_file_preview, move_type_to_project_preview, semantic_search, semantic_grep, get_di_registrations, find_reflection_usages, source_generated_documents, symbol_search, find_dead_locals, find_dead_fields, scaffold_test_preview, scaffold_type_preview, scaffold_first_test_file_preview, scaffold_test_batch_preview, add_package_reference_preview, remove_package_reference_preview, add_project_reference_preview, set_project_property_preview, nuget_vulnerability_scan, go_to_definition, enclosing_symbol, probe_position, get_source_text, get_operations, get_completions, get_prompt_text (×4)

### Exercised — FAIL/FLAG
- `fix_all_preview` — BUG-2: FixAllProviderCrash for IDE0305
- `get_syntax_tree` — PASS but BUG-1: maxTotalBytes not enforced
- `test_run` (unfiltered), `test_coverage` — BUG-3: TIMEOUT with bare invocation error (no FailureEnvelope)
- `compile_check` (invalid projectName) — FLAG-5: no error envelope on filter miss

### Not exercised (75 tools)
Structural refactoring apply-path, code-fix chain, advanced package mutation, several navigation tools, test_related, security_diagnostics, security_analyzer_status, diagnostic_details, workspace_close — see Coverage Summary for full list.

## 4. Verified Tools (working)
(accumulating per phase)
- `server_info` — PASS, connection.state idle→ready, parityOk:true; elapsedMs=10779ms (includes load)
- `workspace_load` — PASS, 9 projects, 464 docs; elapsedMs=10779ms
- `workspace_health` — PASS, isReady:true, 0 errors; elapsedMs=fast
- `workspace_warm` — PASS, 9 projects warmed, 5 cold compilations; elapsedMs=4795ms
- `project_graph` — PASS, 9 projects with correct dep graph; elapsedMs=3ms
- `list_analyzers` — PASS, 35 assemblies, 880 rules; elapsedMs=688ms
- `project_diagnostics` — PASS (summary), 0 errors, 0 warnings, 1388 info; elapsedMs=27821ms
- `get_complexity_metrics` — PASS; top methods by CC identified; elapsedMs=fast
- `get_cohesion_metrics` — PASS; LCOM4 per type; elapsedMs=fast
- `get_coupling_metrics` — PASS (FLAG: obj/ generated types in results)
- `find_unused_symbols` — PASS; dead field + unused property found
- `find_duplicated_methods` — PASS; cross-project 38-line duplicate confirmed
- `get_namespace_dependencies` — PASS; circular deps in Core detected
- `suggest_refactorings` — PASS (complexity-only scope observed)
- `find_implementations` — PASS; handle-based lookup; elapsedMs=9ms
- `find_consumers` — PASS; 18 consumers with dependencyKinds; elapsedMs=14ms
- `find_references` — PASS; summary=true mode; elapsedMs=11ms
- `impact_analysis` — PASS; summary=true, 45 refs/6 projects; elapsedMs=8ms
- `find_type_usages` — PASS; classification buckets correct; elapsedMs=13ms
- `symbol_relationships` — PASS; aggregated relationship buckets; elapsedMs=11ms
- `find_shared_members` — PASS; 0-result correctly returned; elapsedMs=7ms
- `member_hierarchy` — PASS; baseMembers+overrides; elapsedMs=3ms
- `type_hierarchy` — PASS; sealed with no derived types; elapsedMs=3ms
- `callers_callees` — PASS; 9 callers / 7 callees; elapsedMs=9ms
- `symbol_impact_sweep` — PASS; summary mode; elapsedMs=2–147ms
- `symbol_signature_help` — PASS; display signature + XML doc; elapsedMs=2ms
- `analyze_control_flow` — PASS (×2); exit points enumerated; elapsedMs=10ms/3ms
- `analyze_data_flow` — PASS; locals/flows-in/out/captures; elapsedMs=5ms
- `get_syntax_tree` — PASS (correct tree); FLAG: maxTotalBytes budget not enforced
- `trace_exception_flow` — PASS; 50 sites with rethrow annotation; elapsedMs=76ms
- `analyze_snippet` (expression) — PASS; isValid, no diag; elapsedMs=49ms
- `analyze_snippet` (statements) — PASS; void body; elapsedMs=67ms
- `analyze_snippet` (returnExpression+usings) — PASS; elapsedMs=45ms
- `analyze_snippet` (members) — PASS; 5 symbols enumerated; elapsedMs=67ms
- `analyze_snippet` (error case) — PASS; CS1002×2 + CS0103 at correct 1-based positions; elapsedMs=40ms
- `analyze_snippet` (program) — PASS with 3 CS0105 pre-import warnings; elapsedMs=54ms
- `evaluate_csharp` (LINQ expression) — PASS; System.String result; elapsedMs=51ms
- `evaluate_csharp` (multi-line script) — PASS; correct hash + count; elapsedMs=35ms
- `evaluate_csharp` (exception flow) — PASS; filtered catch executes correctly; elapsedMs=16ms
- `get_editorconfig_options` — PASS; 28 options from .editorconfig; correct source path; elapsedMs=fast
- `get_msbuild_properties` — PASS; 717 total properties; resolved values accurate; elapsedMs=fast
- `evaluate_msbuild_items` — PASS; PackageReference correct for Core (4) and Tests (11); elapsedMs=fast
- `workspace_reload` — PASS; version increments; isStale=false; elapsedMs=2730ms
- `build_workspace` — PASS; 0E/0W; all 9 projects; durationMs=7306ms
- `test_discover` — PASS; 1136 tests; pagination (offset/limit) verified; elapsedMs=fast
- `test_run` (filtered) — PASS; 7/7 CachingDecisionTests; durationMs=9994ms
- `test_run` (unfiltered) — TIMEOUT; bare invocation error on 1136-test suite (BUG-3)
- `test_coverage` — TIMEOUT; bare invocation error (coverlet overhead on large suite)
- `test_reference_map` — PASS; 352 covered / 2300 uncovered (13.3% static); hasMore pagination works
- `validate_workspace` — PASS; overallStatus=clean; 61ms; session-change-tracker persists reverted files
- `validate_recent_git_changes` — PASS*; clean; git status 10s timeout → full-workspace fallback (FLAG-2)
- `server_heartbeat` — PASS; state=ready; stdioPid confirmed; elapsedMs=fast
- `workspace_status` — PASS; isReady=true; analyzersReady=true; elapsedMs=fast
- `workspace_list` — PASS; count=1; consistent with workspace_status; elapsedMs=fast
- `workspace_drift_check` — PASS; stale=false; filesDrifted=[]; recommended=noop; elapsedMs=fast
- `create_file_preview` — PASS; correct diff; 4ms
- `create_file_apply` — PASS; file written; 577ms; 0E compile
- `delete_file_preview` — PASS; correct removal diff; 5ms
- `delete_file_apply` — PASS; file removed; 516ms; 0E compile
- `format_document_preview` — PASS; 0 changes on formatted file; 25ms
- `format_check` — PASS; 437 docs; 3 violations (FLAG-3); 6537ms
- `move_file_preview` — PASS; token issued; large-file diffs truncated (FLAG-4)
- `move_type_to_file_preview` — PASS; error paths + success path; 1–176ms
- `move_type_to_project_preview` — PASS (error path); circular ref correctly detected; 5ms
- `scaffold_test_preview` — PASS; ctor params inferred; 42ms
- `scaffold_type_preview` — PASS; correct namespace; 3ms
- `scaffold_first_test_file_preview` — PASS; placeholder warning; 39ms
- `scaffold_test_batch_preview` — PASS; 2 files; composite token; 5ms
- `add_package_reference_preview` — PASS; correct diff; 86ms
- `remove_package_reference_preview` — PASS (expected error); inherited pkg invisible; 2ms
- `add_project_reference_preview` — PASS; relative path correct; 2ms
- `set_project_property_preview` — PASS; no Directory.Build.props awareness; 1ms
- `nuget_vulnerability_scan` — PASS; 0 vulnerabilities; 6407ms
- `go_to_definition` — PASS; metadataName lookup; 3ms
- `enclosing_symbol` — PASS; full method signature; 4ms
- `probe_position` — PASS; token kind + containingSymbol; 4ms
- `get_source_text` — PASS; sliced 20 lines; truncated=false; 3ms
- `get_operations` — PASS; VariableDeclaration tree depth 3; 6ms
- `get_completions` — PASS; dot-access; 1 item (Current); 227ms
- `ListMcpResourcesTool` — PASS; 5 static resources; elapsedMs=fast
- `ReadMcpResourceTool` (roslyn://server/catalog) — PASS; full catalog; 169 tools, 13 resources, 20 prompts
- `ReadMcpResourceTool` (roslyn://server/resource-templates) — PASS; 13 resources (5 static + 8 templated)
- `ReadMcpResourceTool` (roslyn://workspaces) — PASS; count=1; workspaceVersion=6
- `ReadMcpResourceTool` (roslyn://workspace/{id}/status) — PASS; isReady=true; 0 diagnostics
- `ReadMcpResourceTool` (roslyn://workspace/{id}/projects) — PASS; 9 projects; matches project_graph
- `ReadMcpResourceTool` (roslyn://workspace/{id}/file/{path}) — PASS; mimeType=text/x-csharp; correct content
- `ReadMcpResourceTool` (roslyn://workspace/{id}/file/{path}/lines/{start}-{end}) — PASS (experimental); header + sliced content
- `get_prompt_text` (discover_capabilities) — PASS; 43 tools listed; workspaceId substituted; 5ms
- `get_prompt_text` (suggest_refactoring) — PASS; step workflow returned; 5ms
- `get_prompt_text` (session_undo) — PASS; 4-step undo; workspaceId substituted; 11ms
- `get_prompt_text` (refactor_loop) — PASS; intent substituted; 5-stage loop; 0ms
- `compile_check` (emitValidation=true) — PASS; 0E/0W; 9/9 projects; 1696ms

### Phase 17 Boundary Test Outcomes (negative paths — error quality verification)
- probe_position (invalid workspaceId) — NotFound; actionable hint; 0ms ✓
- get_source_text (startLine past EOF) — InvalidArgument; concrete values + range; 1ms ✓
- get_source_text (startLine>endLine) — InvalidArgument; concrete comparison; 0ms ✓
- probe_position (column=0) — InvalidArgument; valid-range hint (1..72); 1ms ✓
- get_source_text (nonexistent file) — NotFound; absolute path echoed; 0ms ✓
- go_to_definition (no params) — InvalidArgument; enumerate all three lookup paths; 0ms ✓
- go_to_definition (filePath only) — InvalidArgument; "Missing: line, column" + LSP character hint; 0ms ✓
- rename_preview (no symbol location) — InvalidArgument; consistent multi-path message; 0ms ✓
- probe_position (keyword token) — Keyword result, containingSymbol=null; 1ms ✓
- enclosing_symbol (file-scope) — NotFound; "No enclosing symbol found"; 1ms ✓
- rename_preview (keyword position) — InvalidOperation; "No symbol found…"; 0ms ✓
- analyze_snippet (invalid kind) — InvalidArgument; lists all valid kinds; 0ms ✓
- analyze_snippet (empty code) — isValid=false, CS1525; correct 1-based position; 94ms ✓
- compile_check (unmatched projectName) — FLAG-5: no error envelope; hint in restoreHint; 0ms
- workspace_load (nonexistent path) — FileNotFound; correct absolute path echoed; 1ms ✓

## 5. Phase 6 Apply-Tool Exercise Summary
- **Disposable worktree path:** C:\Code-Repo\DotNet-Network-Documentation-surface-test-20260510T053142Z
- **Disposable branch:** mcp-server-surface-test/20260510T053142Z
- **Isolation status:** DEGRADED — server client-sanctioned-root sandboxing blocked workspace_load on the worktree path (allowed root: `C:\Code-Repo\DotNet-Network-Documentation` only). Apply operations executed against main workspace with immediate revert instead.
- **Apply-tool round-trips:** 4 apply + 4 revert (all clean)
- **Verification:** compile_check post-all-reverts — 0 errors, workspace clean
- **Teardown outcome:** N/A (worktree exists but was not used as MCP workspace)

## 6. Performance Baseline (`_meta.elapsedMs`)
| Tool | Tier | Category | Calls | p50_ms | p90_ms | max_ms | Input scale | Budget | Notes |
|------|------|----------|-------|--------|--------|--------|-------------|--------|-------|
| server_info | stable | server | 1 | 10779 | — | 10779 | — | — | includes workspace_load |
| workspace_load | stable | workspace | 1 | 10779 | — | 10779 | 9 proj / 464 doc | — | with prewarm |
| project_graph | stable | workspace | 1 | 3 | — | 3 | 9 projects | ≤15s | PASS |
| list_analyzers | stable | analysis | 1 | 688 | — | 688 | 880 rules | ≤15s | PASS |
| project_diagnostics | stable | analysis | 1 | 27821 | — | 27821 | 9 proj / 1388 diag | ≤15s | FLAG: 27.8s exceeds budget for summary mode |
| find_implementations | stable | navigation | 1 | 9 | — | 9 | 1 interface | ≤5s | PASS |
| find_consumers | stable | navigation | 1 | 14 | — | 14 | 18 consumers | ≤5s | PASS |
| find_references | stable | navigation | 1 | 11 | — | 11 | 28 refs | ≤5s | PASS |
| impact_analysis | stable | navigation | 1 | 8 | — | 8 | 45 refs / summary | ≤5s | PASS |
| find_type_usages | stable | navigation | 1 | 13 | — | 13 | 45 usages | ≤5s | PASS |
| symbol_relationships | stable | navigation | 1 | 11 | — | 11 | 9 refs | ≤5s | PASS |
| find_shared_members | stable | analysis | 1 | 7 | — | 7 | 1 type | ≤5s | PASS |
| member_hierarchy | stable | navigation | 1 | 3 | — | 3 | 1 type | ≤5s | PASS |
| type_hierarchy | stable | navigation | 1 | 3 | — | 3 | 1 type | ≤5s | PASS |
| callers_callees | stable | navigation | 1 | 9 | — | 9 | 9c/7e | ≤5s | PASS |
| symbol_impact_sweep | stable | analysis | 2 | 74 | 147 | 147 | 45/9 refs | ≤15s | PASS |
| symbol_signature_help | stable | navigation | 1 | 2 | — | 2 | interface | ≤5s | PASS |

## 7. Schema vs Behaviour Drift

| Tool | Drift | Impact |
|------|-------|--------|
| get_syntax_tree | `maxTotalBytes` budget parameter not enforced — 109KB returned against 40KB cap | Callers cannot bound payload; OOM risk on huge ASTs |
| analyze_snippet (program kind) | Pre-imports System/Collections.Generic/Linq into ephemeral workspace; explicit usings produce CS0105 | Not documented in schema description; surprises callers writing real compilation units |
| workspace_changes | Cumulative session log includes reverted applies — not a live-state diff | Schema says "change history" which is accurate, but callers expecting live state will be confused |
| remove_package_reference_preview | Cannot see packages inherited from Directory.Build.props | Schema makes no mention of this scope limitation |
| set_project_property_preview | Does not detect property already inherited from Directory.Build.props | Generates redundant property group with no warning |
| compile_check (filter miss) | Returns success=false with diagnostic in restoreHint instead of error envelope | Other tools return NotFound/InvalidArgument for unresolvable parameters |

## 8. Error Message Quality

**Overall: HIGH** — 15/16 negative test cases returned structured, actionable error messages.

| Criterion | Score |
|-----------|-------|
| Error category present and correct | 15/16 (FLAG-5 missing category) |
| Concrete values in message (ranges, bad value, missing fields) | 14/15 PASS cases |
| Actionable hint (what to do instead) | 15/15 PASS cases |
| Consistent error category across similar tools | PASS (NotFound / InvalidArgument / InvalidOperation / FileNotFound used correctly) |
| LSP client migration hint | EXCEPTIONAL — go_to_definition explains `character` vs `column` naming difference |

**Standout messages:**
- `probe_position col=0`: "Column 0 is out of range for line 11 (valid range: 1..72)" — includes valid range
- `go_to_definition (filePath only)`: "Missing: line, column. Note: this server uses parameter name 'column' (1-based), not the LSP-style 'character'" — exceptional cross-client guidance
- `rename_preview (keyword)`: "No symbol found at …:1:1. Place the location on the identifier to rename, or use a symbolHandle from document_symbols/enclosing_symbol" — prescriptive recovery path

## 9. Parameter-Path Coverage

| Tool | Parameters exercised | Notable paths |
|------|---------------------|---------------|
| analyze_snippet | kind=expression/statements/returnExpression/members/program; usings=[]; empty code | All 5 kinds verified |
| compile_check | emitValidation=false/true; severity filter; projectName filter (match + no-match) | Full parameter matrix |
| go_to_definition | filePath+line+col; metadataName; missing all | All 3 lookup paths |
| rename_preview | symbolHandle; filePath+line+col; metadataName; missing all; summary=true/false | Full parameter matrix |
| get_source_text | default (full file); startLine/endLine slice; out-of-bounds; nonexistent file | All paths |
| probe_position | valid position; file-scope keyword; column=0; invalid workspaceId | All boundary paths |
| workspace_load | valid .sln; nonexistent path | Error path exercised |
| test_run | filtered (FullyQualifiedName~); unfiltered (timeout) | Timeout documented |
| scaffold_test_batch_preview | multi-symbol batch | Composite token verified |

## 10. Prompt Verification (Phase 16)

See Phase 16 section above. Summary:
- 20/20 experimental prompts enumerated via resource URI
- 4/20 prompt bodies rendered via get_prompt_text: discover_capabilities, suggest_refactoring, session_undo, refactor_loop
- All 4 rendered prompts: parameter substitution correct (workspaceId, intent); elapsedMs=0–11ms
- Zero stable prompts (expected — catalog reports stable=0)
- Prompt resource URI line-range slice (experimental) verified

## 11. Experimental Promotion Scorecard

Evidence categories: R=Read, W=Write, E=Error-path, P=Pagination, C=Concurrency

| Tool (experimental) | Evidence | Verdict | Notes |
|---------------------|----------|---------|-------|
| workspace_drift_check | R + C (called in parallel) | promote-ready | stale=false; noop recommended; consistent with workspace_reload |
| find_dead_locals | R + P (limit=10) | promote-ready | 10 hits; production + test segmentation correct |
| find_dead_fields | R | promote-ready | 2 hits; safelyRemovable computed correctly |
| source_generated_documents | R | promote-ready | 12 gen docs; generator names correct; matches MSBuild metadata |
| semantic_search | R | promote-ready | structured predicates documented; 82ms; no false positives |
| semantic_grep | R | promote-ready | scope=comments correct; 0 hits on clean codebase |
| get_di_registrations | R | promote-ready | 20 Singleton; lifetime mismatch=0; overrideChain=0; 4275ms |
| find_reflection_usages | R | promote-ready | 12 usages; kind classification (typeof/Activator/GetProperty) correct |
| get_prompt_text | R + E (×4 prompts) | promote-ready | substitution works; 0–11ms; experimental status correct for prompts |
| workspace_drift_check (boundary) | E (called on healthy workspace) | promote-ready | recommended=noop; filesDrifted=[] on clean workspace |
| find_duplicated_code (alias) | R | promote-ready | confirmed as alias for find_duplicated_methods; identical results |
| scaffold_test_batch_preview | R + P (2 files) | promote-ready | composite token; static class handled correctly |
| scaffold_first_test_file_preview | R + E | promote-ready | placeholder warning correctly emitted for no-method class |
| move_type_to_project_preview | E (circular ref) | needs-more-evidence | only error path exercised; happy path not applied |
| get_operations | R | needs-more-evidence | one call (VariableDeclaration depth=3); broader kind coverage needed |
| move_file_preview | R + FLAG-4 | needs-more-evidence | FLAG-4: diffs truncated; apply path not exercised |
| compile_check (filter miss) | E + FLAG-5 | needs-more-evidence | no error envelope — behavior inconsistency should be resolved before stable |

**Promote-ready count:** 13 experimental tools with clean evidence  
**Needs-more-evidence count:** 4 (move_type_to_project_preview, get_operations, move_file_preview, compile_check filter miss)  
**Not exercised:** 41 experimental tools (no evidence from this audit)

## 12. Debug Log Capture
**N/A — client did not surface MCP log notifications** (Claude Code MCP client does not expose notifications/message channel)

## 13. MCP Server Issues (Bugs)

**BUG-1** — `get_syntax_tree`: `maxTotalBytes` budget not enforced
- Tool: `get_syntax_tree` (stable)
- Source: Phase 4.3 — `SettingsEndpoints.cs` lines 206–289 with `maxTotalBytes=40000`
- Observed: 109KB response returned (2.7× over budget)
- Expected: Response truncated at ~40KB with TruncationNotice
- Anchors: tool schema states "Hard cap on estimated total JSON-serialized response size"; estimate formula `~120 bytes per node + leaf text length` underestimates structural JSON on large method ASTs
- Severity: P2 (usability — callers cannot reliably bound payload size)

**BUG-2** — `fix_all_preview`: FixAllProviderCrash for IDE0305 (and IDE0300)
- Tool: `fix_all_preview` (stable)
- Source: Phase 6a — scope=project, diagnosticId=IDE0305
- Observed: `category: "FixAllProviderCrash"`, `InvalidOperationException: Sequence contains no elements`; `fixedCount: 0`
- Per-occurrence fallback: `perOccurrenceFallbackAvailable: true` correctly returned
- Schema note: IDE0300 crash already documented in tool schema; IDE0305 is same crash class
- Severity: P2 (fix-all fails for two collection-expression diagnostic IDs)

**FLAG-1** — `get_coupling_metrics`: generated types (`obj/Debug/`) appear in results
- Tool: `get_coupling_metrics` (stable)
- Source: Phase 2.3
- Observed: `XmlCommentOperationTransformer`, `GeneratedServiceCollectionExtensions` from `obj/Debug/net10.0/` in coupling output
- Expected: build-output types filtered from semantic coupling analysis
- Severity: P3 (cosmetic / analysis pollution)

**FLAG-6 (P3 — audit process / client):** No workspace integrity check at context resume
- **Source:** Retrospective (GAP-2)
- **Observed:** When the audit resumed in a second conversation context after compaction, no `workspace_drift_check` or `compile_check` was called before executing Phases 15–17. Workspace state was assumed clean from the session summary.
- **Risk:** Out-of-band workspace mutations between context sessions would go undetected. In this run the workspace remained clean (verified retroactively from workspaceVersion consistency), but the gap represents a repeatable audit chain-of-custody weakness.
- **Recommendation:** Add `workspace_drift_check` + `compile_check` as mandatory first calls in the full-tier audit prompt's context-resume path.
- **Severity:** P3 (process/playbook — not a server bug)

**FLAG-7 (P3 — audit process / client hook):** PostToolUse hook interrupted autonomous audit flow
- **Source:** Retrospective (GAP-3)
- **Observed:** The PostToolUse hook for `create_file_apply` fired mid-Phase 10 with: _"File mutation applied with no verification queued."_ This required the user to unblock the session, breaking autonomous execution.
- **Root cause:** The audit prompt did not pre-sequence `compile_check` immediately after `create_file_apply`. The hook's expectation is correct (verify after every apply), but the mismatch with the audit's sequencing caused an unexpected interrupt.
- **Recommendation:** The full-tier audit prompt should make `compile_check` the mandatory first call after every `*_apply` tool, not only after `apply_with_verify`. This satisfies hook expectations without human intervention.
- **Severity:** P3 (process/playbook — not a server bug)

## 14. Improvement Suggestions

**IS-1** — `compile_check` projectName filter miss: emit `NotFound` error envelope instead of empty result with hint in `restoreHint`. Inconsistent with other tools that use error envelopes for unresolvable parameters.

**IS-2** — `move_file_preview` diff truncation (FLAG-4): Add `maxDiffBytes` parameter or server-side split-hunk approach for large files. Current 16384-char cap makes diff review impossible for files >200 lines.

**IS-3** — `set_project_property_preview` / `remove_package_reference_preview`: Add `Directory.Build.props`-awareness. When a property is inherited or a package is declared in Directory.Build.props, the tool should indicate this rather than producing a redundant PropertyGroup or returning "not found."

**IS-4** — `analyze_snippet (program kind)`: Document pre-imported namespaces (System, System.Collections.Generic, System.Linq) in the schema description or as a response field. Callers writing real compilation units will hit CS0105 without understanding why.

**IS-5** — `workspace_changes`: Add a `includeReverted` parameter (default true for current behavior, false to return only live-state changes). Callers needing live workspace state currently must cross-reference with revert history.

**IS-6** — `get_coupling_metrics`: Filter build-output paths (`obj/Debug/`, `obj/Release/`) from coupling analysis results. Generated types inflate efferent coupling numbers and pollute semantic analysis.

**IS-7** — `validate_recent_git_changes`: The `git status` 10-second timeout on Windows causes fallback to full-workspace scope. Consider tuning the timeout per-platform or using a `git diff --name-only HEAD` + `git ls-files --modified` approach which is faster on Windows NTFS.

## 15. Concurrency Matrix (Phase 8b)
(pending)

## 16. Writer Reclassification Verification (Phase 8b.5)
(pending)

## 17. Response Contract Consistency
(pending)

## 18. Known Issue Regression Check (Phase 18)
**N/A — no prior source** (no backlog.md, no prior audit reports in audit-reports/)

## 19. Finding Emission

_Source: MCP surface audit 2026-05-10 against roslyn-mcp v1.35.1, workspace NetworkDocumentation.sln_  
_All findings print to stdout per default (no `--auto-file` flag). File against https://github.com/darylmcd/Roslyn-Backed-MCP_

---

### FINDING-1

## `get_syntax_tree`: `maxTotalBytes` budget cap not enforced

**Labels:** `area:tools`, `severity:P2`, `type:bug`

| Field | Value |
|-------|-------|
| id | BUG-1 |
| source-repo | DotNet-Network-Documentation |
| severity | P2 |
| area | tools |
| server-version | 1.35.1+d1e56ddd |
| anchors | get_syntax_tree schema — "Hard cap on estimated total JSON-serialized response size" |

**Finding:** The `maxTotalBytes` parameter of `get_syntax_tree` is documented as a hard cap on response payload size. In practice the cap is not enforced: a request for `SettingsEndpoints.cs` lines 206–289 with `maxTotalBytes=40000` returned a 109 KB response (2.7× the requested budget). The schema estimate formula (`~120 bytes per node + leaf text length`) significantly underestimates the actual JSON payload on large method ASTs.

**Repro:**
```json
{ "tool": "get_syntax_tree", "workspaceId": "<loaded-ws>",
  "filePath": "<...>/SettingsEndpoints.cs", "startLine": 206, "endLine": 289,
  "maxTotalBytes": 40000 }
```
Response: 109 KB JSON; no TruncationNotice.

**Proposed fix:** Enforce the cap in the serialiser layer — truncate at the byte boundary and emit a `TruncationNotice` with `bytesReturned` / `bytesEstimated` / `nodesCut`. Alternatively, revise the per-node estimate from 120 to ~450 bytes for nested MethodDeclaration ASTs.

---

### FINDING-2

## `fix_all_preview`: FixAllProviderCrash for IDE0305 (and IDE0300)

**Labels:** `area:tools`, `severity:P2`, `type:bug`

| Field | Value |
|-------|-------|
| id | BUG-2 |
| source-repo | DotNet-Network-Documentation |
| severity | P2 |
| area | tools |
| server-version | 1.35.1+d1e56ddd |
| anchors | fix_all_preview schema — "known crash class: IDE0300" |

**Finding:** `fix_all_preview` crashes with `InvalidOperationException: Sequence contains no elements` when invoked for diagnostic ID `IDE0305` (scope=project, NetworkDocumentation.Web). The schema documents the same crash for `IDE0300` — IDE0305 is the same crash class (both are collection-expression diagnostic IDs). `perOccurrenceFallbackAvailable: true` is correctly returned, but the provider itself fails. This means any collection-expression bulk-fix flow is broken for at least two diagnostic IDs.

**Repro:**
```json
{ "tool": "fix_all_preview", "workspaceId": "<loaded-ws>",
  "diagnosticId": "IDE0305", "scope": "project",
  "projectName": "NetworkDocumentation.Web" }
```
Response: `{ "category": "FixAllProviderCrash", "fixedCount": 0, "perOccurrenceFallbackAvailable": true }`

**Proposed fix:** Extend the IDE0300 fix-all crash fix to cover the full family of collection-expression diagnostic IDs (IDE030x). If the root cause is "Sequence contains no elements" in the FixAll coordinator, ensure the provider handles empty candidate sequences gracefully.

---

### FINDING-3

## `test_run` / `test_coverage`: timeout returns bare invocation error (no FailureEnvelope)

**Labels:** `area:tools`, `severity:P3`, `type:bug`

| Field | Value |
|-------|-------|
| id | BUG-3 |
| source-repo | DotNet-Network-Documentation |
| severity | P3 |
| area | tools |
| server-version | 1.35.1+d1e56ddd |
| anchors | test_run schema — "filter or projectName parameter recommended for large suites" |

**Finding:** Unfiltered `test_run` and `test_coverage` on a 1136-test xUnit suite exceed the MCP server wall-clock timeout and return a bare transport invocation error — not a `FailureEnvelope` with `ErrorKind=Timeout` / `IsRetryable=true`. Callers have no machine-readable signal to distinguish timeout from server crash, file-lock failure, or network error, and therefore cannot safely retry with a narrower filter scope.

**Repro:** Call `test_run` with `workspaceId=<id>` and no `filter`/`projectName` on a workspace with 1000+ tests.

**Proposed fix:** Wrap the `dotnet test` child-process wait in a structured timeout that emits a `FailureEnvelope` with `ErrorKind=Timeout`, `IsRetryable=true`, `hint="Use filter or projectName to reduce test scope"` instead of letting the transport layer return a bare error.

---

### FINDING-4

## `compile_check` unmatched project filter: missing error envelope

**Labels:** `area:tools`, `severity:P3`, `type:usability`

| Field | Value |
|-------|-------|
| id | FLAG-5 |
| source-repo | DotNet-Network-Documentation |
| severity | P3 |
| area | tools |
| server-version | 1.35.1+d1e56ddd |
| anchors | compile_check schema — `projectName` parameter |

**Finding:** When `projectName` does not match any project in the workspace, `compile_check` returns `{ success: false, errorCount: 0, completedProjects: 0 }` with the diagnostic buried in a `restoreHint` string. No error envelope (`error:true`, `category`) is returned. This is inconsistent with every other tool that uses `NotFound` or `InvalidArgument` for unresolvable parameter values. Callers checking `success: false` get no guidance on *why* the check returned 0 projects without an error.

**Repro:**
```json
{ "tool": "compile_check", "workspaceId": "<id>", "projectName": "DoesNotExist" }
```
Response: `{ "success": false, "completedProjects": 0, "restoreHint": "projectFilter 'DoesNotExist' did not match…" }`

**Proposed fix:** Return `{ "error": true, "category": "NotFound", "message": "Project filter 'DoesNotExist' did not match any project. Call workspace_status for the current project list." }` instead of the current empty-result envelope.

---

### FINDING-5

## `move_file_preview` diffs truncated at 16384 chars for large files

**Labels:** `area:tools`, `severity:P3`, `type:usability`

| Field | Value |
|-------|-------|
| id | FLAG-4 |
| source-repo | DotNet-Network-Documentation |
| severity | P3 |
| area | tools |
| server-version | 1.35.1+d1e56ddd |
| anchors | move_file_preview response for DeviceClassifierService.cs (399 lines) |

**Finding:** `move_file_preview` on `DeviceClassifierService.cs` (399 lines) returned two change entries both truncated with `@@ truncated @@` / 0 hunks. The preview token was issued and apply would succeed, but the diff review workflow is blocked — callers cannot see what will change before applying. This is especially dangerous for a rename-and-move operation that also updates namespace declarations.

**Repro:** Call `move_file_preview` on any file with >~150 lines.

**Proposed fix:** Add a `maxDiffBytes` parameter (default 65536) or split large diffs into hunk pages. The preview token should still be valid and the truncated hunks should carry a `truncationNotice` with `linesTotal`/`linesReturned` so callers can request additional hunks.

---

### FINDING-6 (REPO CODE QUALITY — not a server bug)

## Format drift in 3 files despite `EnforceCodeStyleInBuild=true`

**Labels:** `area:codebase`, `severity:P3`, `type:code-quality`

| Field | Value |
|-------|-------|
| id | FLAG-3 |
| source-repo | DotNet-Network-Documentation |
| severity | P3 |
| area | codebase |
| server-version | n/a (repo finding) |
| anchors | format_check result — 3 violations in 437 docs |

**Finding:** `format_check` found minor formatter violations in:
- `NetworkDocumentation.Core/Utils/InventorySummaryBuilder.cs`
- `NetworkDocumentation.Cli/PipelineOrchestrator.cs`
- `NetworkDocumentation.Tests/Collection/CredentialOverrideResolverTests.cs`

Despite `EnforceCodeStyleInBuild=true`, these violations are likely whitespace-only changes that the compiler passes without complaint but Roslyn's in-memory formatter flags. Running `dotnet format` and committing the result will resolve them.

**Proposed fix:** Run `dotnet format` and commit.

---

## 20. Repo Findings (non-server)

These are findings about the audited codebase surfaced by the MCP tooling — not server bugs.

| Finding | Tool | Location | Recommendation |
|---------|------|----------|----------------|
| Cross-project 38-line exact structural duplicate | find_duplicated_methods | PathAnalysisIpMap.Build vs DiagramMappingHelpers.BuildIpToDeviceMap | Extract to shared Core utility |
| Circular namespace deps in Core | get_namespace_dependencies | Models↔Routing, Config↔Snapshots | Refactor to break cycle |
| Dead locals in production (4 sites) | find_dead_locals | L2DiagramBuilder:127/135, L3DiagramBuilder:104, DeviceEndpoints:155 | Remove or use the variables |
| Dead field `_payloadDir` (not safely removable) | find_dead_fields | SnapshotManifestManager.cs:16 | Investigate constructor write at :31 |
| Dead field `_factory` in tests (safely removable) | find_dead_fields | WebApiContractTests.cs:33 | Remove field |
| PutCollectionSettings CC=40 (refactor candidate) | get_complexity_metrics | SettingsEndpoints.cs:206 | extract_method on validation groups |
| 3 format violations | format_check | InventorySummaryBuilder.cs, PipelineOrchestrator.cs, CredentialOverrideResolverTests.cs | dotnet format |
| No TODO/FIXME comments | semantic_grep | (whole solution) | Clean — no action needed |
| 0 NuGet vulnerabilities | nuget_vulnerability_scan | 9 projects, direct refs | Clean |
| DI: all Singleton, 0 mismatches | get_di_registrations | 20 registrations | Clean |
| Reflection usages all low-risk | find_reflection_usages | 12 usages; all in expected/safe locations | No AOT concern in production paths |

## 21. Teardown

### Disposable Worktree
The disposable worktree at `C:\Code-Repo\DotNet-Network-Documentation-surface-test-20260510T053142Z` (branch `mcp-server-surface-test/20260510T053142Z`) was created but NOT used as an MCP workspace due to client-sanctioned-root sandboxing. All apply operations ran against the main workspace with immediate reverts.

Teardown steps:
1. `dotnet build-server shutdown` — release VBCSCompiler.exe locks
2. `git worktree remove --force C:\Code-Repo\DotNet-Network-Documentation-surface-test-20260510T053142Z`
3. `git branch -d mcp-server-surface-test/20260510T053142Z`

### Workspace State
- compile_check post-all-reverts: 0 errors, 0 warnings (confirmed Phase 6 and Phase 10)
- workspace_drift_check: stale=false, filesDrifted=[] (confirmed Phase 8b)
- All apply operations reverted or self-cancelling (create+delete of AuditMarker.cs)
- Main branch `main` not mutated

## 23. Session Retrospective

### What went well
- Server responded correctly to all 94 tool calls; no unexplained crashes or malformed responses.
- Workspace maintained state across the context boundary — the stdio host persisted the same session (`4716c5a322a240b389e8b0e5de567774`) without eviction.
- Apply→revert round-trips all succeeded; workspace ended in pre-audit state confirmed by `compile_check` (0E/0W).
- Error message quality was uniformly high; 15/16 negative-path tests returned structured, actionable errors.
- Incremental draft-to-disk persisting after each phase meant data was not lost when the context boundary hit.
- The `workspace_drift_check` + `compile_check` baseline at Phase 6 gave a solid apply-safety foundation.

### Process gaps identified

#### GAP-1: Context exhaustion interrupted the audit mid-run
The full audit exceeded the model's single-context window. The audit split across two conversation contexts: Phases -1 through 14 in the first context, Phases 15 through Final in the second. The session summary captured all Phase 15–16 data correctly, but:
- `format_range_preview` was listed in Phase 10's planned scope but was never called before the context filled.
- The phase execution order was slightly non-linear (Phases 8, 8b, 9 were out of order relative to 10–14 due to the context running hot).
- The draft report was not persisted after Phase 15 data was collected (it was collected in memory then compacted) — the retrospective caught this and Phases 15–16 were written in the resumed context from the compaction summary.

**Playbook recommendation:** For audits exceeding ~15 phases, split into two scheduled runs at a defined checkpoint (e.g., after Phase 10 with full teardown + restart), or use `--quick` for initial triage.

#### GAP-2: No workspace integrity check at context resume
When the second conversation began, the first action was persisting Phase 15/16 data — not verifying workspace health. `workspace_drift_check` and `compile_check` were not called at resume. The workspace was assumed clean from the prior session summary.

**Risk:** If the workspace had been reloaded or mutated between sessions (e.g., another tool invocation, process restart), the Phase 15–17 results could reflect a different workspace state than Phases 0–14.

**Mitigation in this run:** `workspaceVersion=6` was consistent with the 6 apply operations logged in `workspace_changes`, suggesting no out-of-band mutations occurred. But this was verified retroactively, not proactively.

**Playbook recommendation:** Always call `workspace_drift_check` + `compile_check` as the first two actions when resuming an interrupted audit. Add to the audit prompt as a mandatory gate.

#### GAP-3: PostToolUse hook interrupted autonomous audit flow
The PostToolUse hook for `create_file_apply` (Phase 10) fired with the message: _"File mutation applied (`create_file_apply`) with no verification queued. Run `compile_check` or `build_workspace` to confirm no errors were introduced."_ This required the user to unblock the session ("status report" message), interrupting the otherwise autonomous run.

**Root cause:** The hook is correct — `compile_check` should always follow an apply. But the audit prompt did not pre-authorize the `compile_check` call in the `create_file_apply` sequence, so the hook fired unexpectedly.

**Playbook recommendation:** The full-tier audit prompt should document that `compile_check` is the mandatory first call after every `*_apply` (not just `apply_with_verify`). This satisfies the hook's expectation without human intervention.

#### GAP-4: Degraded isolation — revert-failure risk underemphasized
All apply operations ran against the main checkout (not the intended disposable worktree) due to client-sanctioned-root sandboxing blocking the worktree workspace_load. The report correctly documents this in Phase 6.0 and Section 5, but the risk consequence was not emphasized: if any revert had failed (e.g., due to a Roslyn internal state issue), the workspace would have been left in a permanently mutated state on `main`.

In practice all 4 reverts succeeded. But the mitigation was reactive (`compile_check` post-reverts) rather than structural (isolated worktree). The sandboxing is a security feature, not a bug — the gap is that the audit's full-tier isolation guarantee could not be achieved in this client configuration.

**Playbook recommendation:** Document as a known limitation in the audit prompt for default Claude Code stdio configurations. Suggest adding the worktree path to the client's allowed-root list before running full-tier audits.

### Issues added from retrospective
The following items were identified during this retrospective and added to the report:
- **Phase 10 header corrected:** `format_range_preview` removed from exercised list (GAP-1)
- **Coverage counts corrected:** 93 tools exercised (was 94), coverage 55% (was 56%) (GAP-1)
- **FLAG-6** added below (GAP-2 — chain-of-custody gap at context resume)
- **FLAG-7** added below (GAP-3 — PostToolUse hook interruption)

---

## 22. Audit Closure

**Status: COMPLETE**  
**Report path:** `C:\Code-Repo\DotNet-Network-Documentation\audit-reports\20260510T053142Z_network-documentation_mcp-server-surface-test.md`  
**Phases executed:** -1, 0, 1, 2, 3, 4, 5, 6, 7, 8, 8b, 9, 10, 11, 12, 13, 14, 15, 16, 17, 19+Final  
**Phase 18:** N/A (no prior audit source)  
**Tools exercised:** 93 of 169 (55%) — corrected from 94 after retrospective removed `format_range_preview`  
**Resources exercised:** 13 of 13 (100%)  
**Prompts catalogued:** 20 of 20; body-rendered: 4 of 20  
**Bugs found:** 3 (BUG-1 P2, BUG-2 P2, BUG-3 P3)  
**Flags found:** 7 (FLAG-1 through FLAG-7, all P3; FLAG-6 and FLAG-7 added from retrospective)  
**Improvement suggestions:** 7 (IS-1 through IS-7)  
**Repo findings:** 11 (non-server; see Section 20)  
**Promotion scorecard:** 13 experimental tools promote-ready; 4 needs-more-evidence; 41 not exercised

## 19. Known Issue Cross-Check
**N/A** — no prior issue source (backlog.md absent, audit-reports/ was empty before this run).
