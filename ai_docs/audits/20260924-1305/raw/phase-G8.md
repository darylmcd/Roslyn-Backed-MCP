# Phase G8 evidence — Phase 15 (resources) + Phase 16 (prompts)

Run 20260924T130517Z. Server: Darylmcd.RoslynMcp@4.2.1 (dnx). Paths sanitized: `<repo>` = <repo>.

## Method / surface access
| item | value |
|---|---|
| ReadMcpResourceTool | NOT available in this subagent (ToolSearch `select:ReadMcpResourceTool` -> no match). Resources read via raw JSON-RPC harness `g8-res.mjs` spawning the SAME launch as the registered plugin (`dnx Darylmcd.RoslynMcp@4.2.1 --source nuget.org`, cwd=RO worktree, ROSLYNMCP_SANCTIONED_ROOTS=.), which loaded the W_RO slnx itself (harness workspaceId 9950bee8…; process exited). |
| get_prompt_text | Registered server (W_RO bbea96a6…): 3 happy renders + 6 probes. All 20 prompts x2 + prompts/get compare via harness `g8-prompts.mjs` (tools/call get_prompt_text on same binary; harness ws 13abf7e1…). |
| prompts/get int follow-up | `g8-pgint2.mjs` (ws 39514cb0…). All harness processes exited; no orphan dnx left by G8 (checked Win32_Process: remaining dnx trees are parented by other claude.exe sessions). |
| Registered-server baselines | workspace_list, workspace_status(W_RO), project_graph, project_diagnostics(summary), project_diagnostics(CA1822 limit=1), get_source_text(1-10). |

## Registered-server calls
| # | tool | inputs | elapsedMs | verdict | note |
|---|---|---|---|---|---|
| 1 | workspace_list | — | n/a (no _meta) | PASS | 2 ws; W_RO v2 token bbea…:2 |
| 2 | workspace_status | W_RO | n/a (no _meta) | PASS | isReady, 6 proj / 905 docs |
| 3 | project_graph | W_RO | 2 | PASS | 6 projects; identical names/refs/isTest to projects resource |
| 4 | project_diagnostics | W_RO summary=true | 4 | PASS | 0E/0W/4405 Info, 27 ids |
| 5 | get_source_text | WorkspaceTools.cs 1-10 | 89 | PASS | 742 lines; text == resource lines/1-10 body |
| 6 | get_prompt_text | analyze_dependencies | 751 | PASS/FLAG | renders; namespace nodes Take(50) unranked -> 50 external zero-type namespaces shown, project namespaces dropped |
| 7 | get_prompt_text | cohesion_analysis RoslynMcp.Roslyn | 286 | PASS | 9 live tool refs |
| 8 | get_prompt_text | consumer_impact L20 C25 | 336 | PASS | JSON numbers accepted |
| 9 | get_prompt_text | no_such_prompt | 0 | FLAG | "Parameter 'promptName' is invalid. Check that all required parameters are provided…" — vague; source builds "Prompt 'x' not found. Available prompts: …" but ToolErrorHandler fallback redacts it |
| 10 | get_prompt_text | truncated parametersJson | 0 | PASS | actionable: "parametersJson must contain a valid JSON object. Example…" |
| 11 | get_prompt_text | review_file missing filePath | 0 | FLAG | "…must be a JSON object whose properties match the prompt schema. Include every required prompt parameter…" — does not name `filePath` (source names it; redacted) |
| 12 | get_prompt_text | consumer_impact line:"abc" | 0 | PASS | actionable: "property 'line' must be compatible with a JSON number (Int32)" |
| 13 | get_prompt_text | session_undo ws=000… | 0 | PASS (note) | static template renders for unknown workspace (no validation) |
| 14 | get_prompt_text | discover_capabilities taskCategory=nonsense-category | 18 | FLAG | silently returns all 174 tools + 20 prompts "relevant to **nonsense-category**"; no validation/hint |
| 15 | project_diagnostics | CA1822 RoslynMcp.Roslyn limit=1 | 37 | PASS | explain_error input (SingleTestScaffolder.cs:322:9) |

## Phase 15 — resources (harness, same binary)
| resource probe | uri (sanitized) | ms | result | mime | len | json |
|---|---|---|---|---|---|---|
| catalog | roslyn://server/catalog | 15 | OK | application/json | 13897 | true |
| catalog-full | roslyn://server/catalog/full | 9 | OK | application/json | 165529 | true |
| cat-tools-0-5 | roslyn://server/catalog/tools/0/5 | 37 | OK | application/json | 15080 | true |
| cat-tools-170-50 | roslyn://server/catalog/tools/170/50 | 2 | OK | application/json | 1811 | true |
| cat-tools-neg | roslyn://server/catalog/tools/-1/0 | 3 | OK | application/json | 12148 | true |
| cat-tools-abc | roslyn://server/catalog/tools/abc/5 | 22 | ERR -32602: InvalidArgument: Parameter 'offset' is invalid. Check that all required parameters are provided and values match the expected types. (resource: roslyn |  | 0 | false |
| cat-prompts-0-5 | roslyn://server/catalog/prompts/0/5 | 3 | OK | application/json | 5271 | true |
| cat-prompts-18-10 | roslyn://server/catalog/prompts/18/10 | 3 | OK | application/json | 1867 | true |
| templates | roslyn://server/resource-templates | 7 | OK | application/json | 4111 | true |
| workspaces | roslyn://workspaces | 6 | OK | application/json | 870 | true |
| workspaces-verbose | roslyn://workspaces/verbose | 7 | OK | application/json | 3928 | true |
| status | roslyn://workspace/9950bee8a72f4cca872b1f9c2c8a898d/status | 34 | OK | application/json | 774 | true |
| status-verbose | roslyn://workspace/9950bee8a72f4cca872b1f9c2c8a898d/status/verbose | 1 | OK | application/json | 3519 | true |
| projects | roslyn://workspace/9950bee8a72f4cca872b1f9c2c8a898d/projects | 3 | OK | application/json | 2914 | true |
| diagnostics | roslyn://workspace/9950bee8a72f4cca872b1f9c2c8a898d/diagnostics | 19341 | OK | application/json | 638 | true |
| file-enc | roslyn://workspace/9950bee8a72f4cca872b1f9c2c8a898d/file/<repo-enc>%2F.worktrees%2Fsurface-test-20260924T13051 | 62 | OK | text/x-csharp | 43259 | false |
| file-raw | roslyn://workspace/9950bee8a72f4cca872b1f9c2c8a898d/file/<repo>/.worktrees/surface-test-20260924T130517Z-ro/sr | 2 | ERR -32002: Unknown resource URI: 'roslyn://workspace/9950bee8a72f4cca872b1f9c2c8a898d/file/<repo>/.worktrees/surface-test-20260924T130517Z-ro/src/RoslynMcp.Host. |  | 0 | false |
| file-rawbs | roslyn://workspace/9950bee8a72f4cca872b1f9c2c8a898d/file/<repo>\.worktrees\surface-tes | 52 | OK | text/x-csharp | 43259 | false |
| file-enc-bs | roslyn://workspace/9950bee8a72f4cca872b1f9c2c8a898d/file/%3Crepo%3E%5C.worktrees%5Cs | 53 | OK | text/x-csharp | 43259 | false |
| file-missing | roslyn://workspace/9950bee8a72f4cca872b1f9c2c8a898d/file/<repo-enc>%2F.worktrees%2Fsurface-test-20260924T13051 | 133 | ERR -32002: NotFound: The requested item was not found. Ensure the workspace is loaded and the identifier is correct. (resource: roslyn://workspace/9950bee8a72f4c |  | 0 | false |
| file-outside | roslyn://workspace/9950bee8a72f4cca872b1f9c2c8a898d/file/C%3A%2FWindows%2Fwin.ini | 123 | ERR -32002: NotFound: The requested item was not found. Ensure the workspace is loaded and the identifier is correct. (resource: roslyn://workspace/9950bee8a72f4c |  | 0 | false |
| lines-1-10 | roslyn://workspace/9950bee8a72f4cca872b1f9c2c8a898d/file/<repo-enc>%2F.worktrees%2Fsurface-test-20260924T13051 | 58 | OK | text/x-csharp | 382 | false |
| lines-1-10-raw | roslyn://workspace/9950bee8a72f4cca872b1f9c2c8a898d/file/<repo>/.worktrees/surface-test-20260924T130517Z-ro/sr | 1 | ERR -32002: Unknown resource URI: 'roslyn://workspace/9950bee8a72f4cca872b1f9c2c8a898d/file/<repo>/.worktrees/surface-test-20260924T130517Z-ro/src/RoslynMcp.Host. |  | 0 | false |
| lines-10-5 | roslyn://workspace/9950bee8a72f4cca872b1f9c2c8a898d/file/<repo-enc>%2F.worktrees%2Fsurface-test-20260924T13051 | 1 | ERR -32602: InvalidArgument: Parameter 'lineRange' must use the 1-based 'startLine-endLine' format with startLine less than or equal to endLine. (resource: roslyn |  | 0 | false |
| lines-oor | roslyn://workspace/9950bee8a72f4cca872b1f9c2c8a898d/file/<repo-enc>%2F.worktrees%2Fsurface-test-20260924T13051 | 58 | ERR -32602: InvalidArgument: Parameter 'lineRange' starts past the end of the source file. Request a range within the reported line count. (resource: roslyn://wor |  | 0 | false |
| lines-740-750 | roslyn://workspace/9950bee8a72f4cca872b1f9c2c8a898d/file/<repo-enc>%2F.worktrees%2Fsurface-test-20260924T13051 | 45 | OK | text/x-csharp | 110 | false |
| lines-bad | roslyn://workspace/9950bee8a72f4cca872b1f9c2c8a898d/file/<repo-enc>%2F.worktrees%2Fsurface-test-20260924T13051 | 2 | ERR -32602: InvalidArgument: Parameter 'lineRange' must use the 1-based 'startLine-endLine' format with startLine less than or equal to endLine. (resource: roslyn |  | 0 | false |
| diff-v2.3.1_v2.3.2 | roslyn://server/catalog-diff/v2.3.1/v2.3.2 | 2 | ERR -32602: InvalidArgument: Unsupported catalog diff. Request one of the version pairs advertised by the catalog resource. (resource: roslyn://server/catalog-dif |  | 0 | false |
| diff-2.3.1_current | roslyn://server/catalog-diff/2.3.1/current | 20 | OK | application/json | 136085 | true |
| diff-v2.3.1_latest | roslyn://server/catalog-diff/v2.3.1/latest | 8 | OK | application/json | 136084 | true |
| diff-v2.3.2_current | roslyn://server/catalog-diff/v2.3.2/current | 1 | ERR -32602: InvalidArgument: Unsupported catalog diff. Request one of the version pairs advertised by the catalog resource. (resource: roslyn://server/catalog-dif |  | 0 | false |
| diff-4.2.1_current | roslyn://server/catalog-diff/4.2.1/current | 1 | ERR -32602: InvalidArgument: Unsupported catalog diff. Request one of the version pairs advertised by the catalog resource. (resource: roslyn://server/catalog-dif |  | 0 | false |
| diff-invalid | roslyn://server/catalog-diff/9.9.9/0.0.0 | 1 | ERR -32602: InvalidArgument: Unsupported catalog diff. Request one of the version pairs advertised by the catalog resource. (resource: roslyn://server/catalog-dif |  | 0 | false |
| diff-garbage | roslyn://server/catalog-diff/abc/def | 1 | ERR -32602: InvalidArgument: Unsupported catalog diff. Request one of the version pairs advertised by the catalog resource. (resource: roslyn://server/catalog-dif |  | 0 | false |

### Resource checks
| check | result |
|---|---|
| 14/14 resources exercised | catalog, catalog/full, catalog/tools/{o}/{l}, catalog/prompts/{o}/{l}, catalog-diff, resource-templates, workspaces, workspaces/verbose, status, status/verbose, projects, diagnostics, file, file/lines |
| MIME vs content | every JSON resource `application/json` and JSON.parse OK; file + lines `text/x-csharp`, plain C# (PASS) |
| catalog counts | tools 174 / resources 14 / prompts 20 == tools/list, resources+templates (14), prompts/list; catalog prompt parameters match prompts/list names/required |
| tools page -1/0 | clamps to offset 0 limit 1 (known; confirmed, not re-reported) |
| tools page abc/5 | -32602 "Parameter 'offset' is invalid. Check that all required parameters…" — raw "offset must be an integer. Got: 'abc'" (ServerResources.cs ParseSlotInt) redacted by ToolErrorHandler fallback (same root as get_prompt_text) |
| catalog-diff | ONLY 2.3.1 -> current/latest works (136 KB diff, git-tag v2.3.1 e147875 -> 4.2.1). `v2.3.1/v2.3.2` REJECTED though the resource Description says "First supported pair: v2.3.1 -> v2.3.2/current" (ServerResources.cs:80). `v2.3.2/current`, `4.2.1/current`, `9.9.9/0.0.0`, `abc/def` -> "Unsupported catalog diff. Request one of the version pairs advertised by the catalog resource." The catalog resource advertises NO pairs (top-level keys: catalogVersion, productBoundaries, productShape, promptCount, promptsResourceTemplate, resources, summary, supportPolicy, toolCount, toolsResourceTemplate, workflowHints). Raw message naming the supported pair (ServerSurfaceCatalog.cs:298-299) is replaced at ToolErrorHandler.cs:634-637. |
| workspaces vs verbose | same workspaceVersion 1 + snapshotToken (PASS) |
| status vs status/verbose | same workspaceVersion + snapshotToken (PASS). FLAG: verbose NOT a superset — lacks isReady, analyzersReady, restoreHint, solutionFileName, workspaceDiagnosticCount/ErrorCount/WarningCount (serializes raw WorkspaceStatusDto at WorkspaceResources.cs:64; summary uses WorkspaceStatusSummaryDto.From) |
| projects vs project_graph | identical 6 project names, projectReferences, isTestProject (PASS) |
| diagnostics vs project_diagnostics | totals agree (0/0/4405). Resource floors at Warning and says so (`severityNote`) -> 0 rows (by design). Cold first read 19,336 ms (fresh process, first analyzer pass) vs warm tool 4 ms |
| file/{path} forms | URL-encoded fwd-slash OK; URL-encoded backslash OK; raw backslash OK; raw fwd-slash -> -32002 "Unknown resource URI" (slot cannot span '/'). Content == disk (CR-normalized) == get_source_text |
| file missing / outside root | -32002 NotFound generic "requested item was not found…" (acceptable; C:/Windows/win.ini gets same text, no out-of-workspace hint) |
| lines/1-10 | marker `// roslyn://workspace/<id>/file/.../lines/1-10 of 742` + exactly 10 lines (PASS) |
| lines/740-750 | end clamped: marker `lines/740-742 of 742` (PASS) |
| lines/10-5 | -32602 "must use 1-based 'startLine-endLine' format with startLine <= endLine", 1 ms (PASS) |
| lines/99999-100000 | -32602 "starts past the end of the source file" (PASS) |
| lines/abc | -32602 same startLine<=endLine text (slightly misleading, fine) |
| lines raw fwd-slash path | Unknown resource URI (same as file raw) |

## Phase 16 — prompts (get_prompt_text x2 + prompts/get with MCP-spec string args)
| prompt | gpt ms (1st,2nd) | len | idempotent | prompts/get (string args) | pg==gpt | tool refs (all in catalog) | unknown snake_case |
|---|---|---|---|---|---|---|---|
| analyze_dependencies | 4650,836 | 23064 | true | ok 790ms | true | 1 |  |
| cohesion_analysis | 1288,442 | 25921 | true | ok 401ms | true | 9 |  |
| consumer_impact | 1019,53 | 6577 | true | FAIL -32602 Invalid parameters | false | 4 |  |
| dead_code_audit | 565,17 | 1134 | true | ok 18ms | true | 6 |  |
| debug_test_failure | 0,0 | 2362 | true | ok 0ms | true | 6 |  |
| discover_capabilities | 1,0 | 9080 | true | ok 0ms | true | 57 | server_catalog |
| explain_error | 13199,59 | 2754 | true | FAIL -32602 Invalid parameters | false | 0 |  |
| fix_all_diagnostics | 44,0 | 926 | true | ok 0ms | true | 7 |  |
| guided_extract_interface | 104,113 | 6228 | true | ok 139ms | true | 10 |  |
| guided_extract_method | 55,51 | 1939 | true | FAIL -32602 Invalid parameters | false | 7 |  |
| guided_package_migration | 4,8 | 1054 | true | ok 4ms | true | 12 |  |
| msbuild_inspection | 0,0 | 934 | true | ok 0ms | true | 4 |  |
| refactor_and_validate | 889,126 | 2177 | true | FAIL -32602 Invalid parameters | false | 14 |  |
| refactor_loop | 0,0 | 2208 | true | ok 0ms | true | 16 | rolled_back applied_with_errors ai_docs |
| review_complexity | 7,2 | 2560 | true | ok 2ms | true | 9 |  |
| review_file | 311,83 | 45163 | true | ok 81ms | true | 12 |  |
| review_test_coverage | 48,0 | 21150 | true | ok 0ms | true | 8 |  |
| security_review | 0,0 | 2498 | true | ok 0ms | true | 12 |  |
| session_undo | 0,0 | 696 | true | ok 0ms | true | 7 |  |
| suggest_refactoring | 88,91 | 14450 | true | FAIL -32602 Invalid parameters | false | 3 |  |

Unknown snake_case tokens: `server_catalog` = resource name (legit); `rolled_back`, `applied_with_errors` = status values; `ai_docs` = path. **No hallucinated tool names in any of the 20 prompts** (mechanical diff vs ledger-seed.tsv in g8-prompts.mjs). Idempotency 20/20 byte-identical. get_prompt_text text == prompts/get text for all 15 prompts prompts/get could render.

### prompts/get integer-argument defect (g8-pgint2.mjs, valid ws)
| call | result |
|---|---|
| consumer_impact line:"19", column:"21" (MCP spec: prompt argument values are strings) | -32602 Invalid parameters for prompt 'consumer_impact' |
| consumer_impact line:19, column:21 (JSON numbers, off-spec) | OK 6,577 chars |
| suggest_refactoring startLine:"40", endLine:"120" | -32602 |
| suggest_refactoring startLine:40, endLine:120 | OK |
| suggest_refactoring (optional ints omitted) | OK |
| review_file (strings only) | OK |

Affected (int/int? params): consumer_impact, explain_error, guided_extract_method, refactor_and_validate, suggest_refactoring (5/20). prompts/list advertises no types. Root: PromptBindingStageAdapter.cs:81 `JsonSerializer.Deserialize(value.GetRawText(), parameter.ParameterType)` rejects a JSON string for int. No backlog row.

### Negative probes
| probe | get_prompt_text | prompts/get |
|---|---|---|
| unknown prompt | "Parameter 'promptName' is invalid. Check that all required parameters are provided…" (vague) | "Unknown prompt: 'no_such_prompt'" (actionable) |
| empty promptName | same vague text | n/a |
| missing required arg | "…Include every required prompt parameter…" — does not name arg | "Invalid parameters for prompt 'X'…" — does not name arg (known) |
| malformed JSON | actionable (example given) | n/a |
| JSON array `[1,2]` | generic parametersJson text (ok) | n/a |

Root for vagueness: PromptShimTools.cs:67-70 and :203-205 throw specific ArgumentExceptions ("Prompt 'x' not found. Available prompts: …", "missing required parameters in parametersJson: filePath") that ToolErrorHandler.cs:590-593 (parametersJson branch) and :646 (generic fallback) replace; they are plain ArgumentException, not PublicArgumentException (ToolErrorHandler.cs:580).

### discover_capabilities vs live catalog
refactoring: 57 tool refs, all live. `all`/unknown lists 174 tools + 20 prompts == catalog. Unknown category -> `_ => true` (PromptMessageBuilder.cs:87 and :101) returns everything under the bogus label. "Workspace Gate: All tools require a `workspaceId`" (RoslynPrompts.AnalysisWorkflows.cs:133) is false for server_info / server_heartbeat / analyze_snippet / workspace_list / workspace_load. get_prompt_text param description cites nonexistent `list_prompts` (PromptShimTools.cs:57, :47).

### Actionability
analyze_dependencies: data dump + 6 questions, only 1 tool ref (get_namespace_dependencies) — weakest; node cap `Take(NamespaceNodeCap)` unranked (RoslynPrompts.cs:247) shows 50 external zero-type namespaces (DiffPlex, Microsoft.*, ModelContextProtocol.*) and drops RoslynMcp.Roslyn.* / Host.Stdio.Tools namespaces that appear in its own cycle list. All other prompts give concrete preview->apply->compile_check/test_run chains.

### Perf (harness 1st/2nd render ms)
explain_error 13,199 cold / 59 warm; analyze_dependencies 4,650 / 836; cohesion_analysis 1,288 / 442; consumer_impact 1,019 / 53; all others < 1 s. Resource diagnostics cold 19,336 ms; other resources <= 62 ms.
