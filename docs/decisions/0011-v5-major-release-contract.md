# ADR 0011: The 5.0.0 contract

Status: Accepted 2026-10-28 (operator decisions 2026-10-28) for the 5.0.0 release.

## Context

`main` carries nine merged `Changed — BREAKING` fragments, so any release cut from it is a major. 4.4.0 shipped from a maintenance branch. 5.0.0 collects every row parked on the major into one reviewed contract so consumers migrate once. `docs/release-policy.md` requires an ADR and a migration note for each breaking change; this ADR is the single index for all of them. It does not restate the nine earlier ADRs; it links them and records every other 5.0 change with old and new wire behavior.

## Decision

### Previously decided breaking changes (unchanged by this ADR)

| ADR | Subject |
|---|---|
| [0012](0012-editorconfig-write-workspace-boundary.md) | Editorconfig writes confined to the workspace boundary |
| [0015](0015-dispatch-public-argument-diagnostics.md) | Safe actionable argument refusals |
| [0016](0016-malformed-refactoring-input-refusals.md) | Malformed refactoring input refusals |
| [0018](0018-extraction-public-argument-diagnostics.md) | Extraction coordinate bounds and refusals |
| [0019](0019-suppression-public-argument-diagnostics.md) | Suppression severity corrections |
| [0027](0027-validation-scope-and-test-run-diagnostics.md) | Validation scope and test-run diagnostics |
| [0028](0028-editorconfig-assignment-grammar-refusals.md) | Editorconfig assignment grammar refusals |
| [0029](0029-fork-source-session-lifecycle.md) | Fork source-session retention and provider drain |
| [0030](0030-retained-fork-storage-and-lifetime.md) | Retained fork storage and lifetime |

### 1. Four tool aliases removed

`get_symbol_outline`, `find_duplicated_code`, `get_test_coverage_map` and `apply_composite_preview` are removed from dispatch, `tools/list` and the catalog; a call fails as an unknown tool. Surface 175 → 171 tools (110 stable / 61 experimental); the stable-only callable count is 94 → 91.

| Removed | Replacement |
|---|---|
| `get_symbol_outline` | `document_symbols` |
| `find_duplicated_code` | `find_duplicated_methods` |
| `get_test_coverage_map` | `test_coverage` |
| `apply_composite_preview` | `apply_composite` |

Also removed, as 5.0 wire/API changes: the `ToolAliasDeprecation` machinery and the top-level `deprecation` property on every catalog entry and on the `document_symbols`, `find_duplicated_methods` and `test_coverage` responses (always null once the aliases went). Removal is intentional: the machinery was dead, and any future tool consolidation must design its own alias lifecycle under its own ADR rather than inherit a vestigial one. The change ledger (`workspace_changes`) now records composite applies as `apply_composite` (it always recorded `apply_composite_preview`), and the PostToolUse hook matcher lists `apply_composite`.

Migration: call the replacement tools (identical parameters and payloads); stop reading `deprecation`; match `apply_composite` in `workspace_changes` tool names and hook matchers.

### 2. `LocationDto` and the flat location members

| Surface | 4.x | 5.0 |
|---|---|---|
| `LocationDto.endLine` / `endColumn` | `int`, never partial | `int?`, both set or both null together; `filePath`, `startLine`, `startColumn` always present |
| `location` for a start-only span (build output with file and start, no end) | `null` | populated, end fields null |
| `location` rule | non-null only when path and all four coordinates exist | non-null whenever a non-blank path and a start exist |
| `SymbolDto`, `DiagnosticDto`, `TypeUsageDto` flat members (`filePath`, `startLine`, `startColumn`, `endLine`, `endColumn`; `TypeUsageDto` also `containingMember`, `previewText`, `classification`) | serialized beside `location`; `[Obsolete]` since 4.4.0 | removed; `location` only (`TypeUsageDto.location` is always present) |
| `PropertyWriteDto`, `MutationCallerDto` flat members | serialized beside `location`, never deprecated | removed; `location` only, always non-null (`writeKind` / `callerPhase` stay top-level) |
| `analyze_snippet` / `evaluate_csharp` diagnostics | `location: null` | `location` with virtual `filePath` `<snippet>` / `<script>` |

**Amendment of ADR 0001 section 2.** "`Location` is never partially populated" is replaced by: the end span is optional (both end coordinates null together) because producers such as MSBuild output give only a start; path and start are required for a location to exist. `LocationDto` stays strict on path and start.

**`DiagnosticDto.filePath` is kept.** 4.4.0 marked `DiagnosticDto.FilePath` `[Obsolete]`; 5.0 reverses that for diagnostics only. It means "the file this diagnostic is about" and is populated whenever a file is known, including file-only rows (`NU1201`, `MSB1009`, unresolved-analyzer, `WORKSPACE001`) where `location` is null. Where both exist they agree. Workspace-failure rows with no file keep `filePath: null`. The C# constructor is now `(Id, Message, Severity, Category, FilePath, Location = null)`.

**Operator exemption from `docs/release-policy.md:38`, dated 2026-10-28.** `PropertyWriteDto` and `MutationCallerDto` never had a released deprecation window. They are removed in 5.0 anyway because both already carry a complete `location` since the 4.x stage 2, to match the primary DTOs, and to avoid another flat-plus-nested major. A span with a blank path falls back to the owning document's `FilePath`, else its `Name`, so no reference is dropped (also applied to `TypeUsageDto`).

Unchanged: `symbol_search` summary rows keep their compact flat `filePath` / `startLine` / `startColumn`. `MutatingMemberDto` still carries flat `filePath` / `line` and no nested `location`.

Migration: read `location.filePath`, `startLine`, `startColumn`, `endLine`, `endColumn` (accept null for the two end fields) and, for type usages and secondary DTOs, `location.containingMember`, `previewText`, `classification`; read `DiagnosticDto.filePath` for the file of any diagnostic.

### 3. Error categories and envelopes

| Case | 4.x | 5.0 |
|---|---|---|
| Unknown `workspaceId` | `category: NotFound`, `exceptionType: KeyNotFoundException`, `reason: WorkspaceNotFound` | `category: WorkspaceNotFound`, `exceptionType: WorkspaceNotFoundException`, no `reason` |
| Unknown `workspaceId` racing an in-call auto-reload | `category: WorkspaceReloadedDuringCall`, `KeyNotFoundException` | the same plain `WorkspaceNotFound` / `WorkspaceNotFoundException` envelope |
| `resources/read` of an unknown workspace | message prefix `NotFound:` | `WorkspaceNotFound:`; JSON-RPC code unchanged |
| Unknown `projectName`; unresolvable `symbolHandle` / `metadataName` / location in `rename_preview` and `rename_apply` | `category: InvalidOperation`, `InvalidOperationException` | `category: NotFound`, `KeyNotFoundException` (internal carrier `PublicNotFoundException`); messages unchanged in substance |
| Configured sanctioned-root canonicalization fault | `InvalidArgument` | `InvalidOperation` (`PublicInvalidOperationException`), message names `ROSLYNMCP_SANCTIONED_ROOTS`, no path |
| Client-roots RPC failure (protocol 2025-11-25 only; under 2026-07-28 roots resolve client-side through `input_required`) | `InvalidArgument` | `InvalidOperation`, "Path validation failed because the client roots narrowing boundary could not be obtained." |
| Unusable request narrowing root | `InvalidArgument` attributed to `options` | `InvalidArgument` attributed to `narrowingRootPaths` (client roots: `path`) |
| Caller boundary denial | `InvalidArgument` | unchanged: `InvalidArgument`, exact `ArgumentException`, opaque message, no path |

The `reason: "WorkspaceNotFound"` field is removed in 5.0 from the tool envelope and from the `resources/read` message suffix. **Operator exemption from `docs/release-policy.md:38`, dated 2026-10-28:** it never had a deprecation window and is removed anyway because the `WorkspaceNotFound` category now carries the same information. The cost is real: the 4.x product contract told consumers to branch on `reason`. Migration: branch on `category: "WorkspaceNotFound"` and `exceptionType` instead of `reason`. A malformed `symbolHandle` stays `InvalidArgument`. Symbol, file and metadata-name misses stay `NotFound`. `WorkspaceEvicted` is unchanged. Document and type not-found sites that still report `InvalidOperation` are tracked by `document-and-type-not-found-report-invalid-operation`.

`ToolErrorHandler.BuildSafeArgumentMessage` is reduced to the sanctioned-root marker, the public-message marker and the generic fallback. The sniffing arms (ParamName `parametersJson`, `projectName`, `pattern` plus the regex sentinel, "Unsupported catalog diff", file-path-plus-column) are retired; `SemanticGrepService.InvalidRegexSentinel` is removed. Released messages came from public carriers and are unchanged for MCP callers.

Migration: branch on `category == "WorkspaceNotFound"` (or `exceptionType == "WorkspaceNotFoundException"`); treat `NotFound` from project or symbol parameters as "named item does not exist" and stop special-casing `InvalidOperation` for them; treat `InvalidOperation` from path-taking tools as a server configuration or client-roots transport fault, not a bad path argument.

### 4. `server_info.update.updateAvailable` is nullable

The schema changes from `boolean` to `["boolean","null"]` (still required). `true` = a known registry version is strictly newer than the running build, even when the latest check is `pending` or `failed` (a stale cached newer version is still a valid signal, since versions only move forward); `false` only when `checkStatus` is `succeeded` and no newer version exists; `null` = unknown (`neverChecked`, `pending`, `failed`, `timedOut` with no newer version known). 4.x emitted `false` in every non-true state. `checkStatus` describes the latest check, not the validity of `updateAvailable`.

Migration: treat `null` as unknown, not "no update"; read `update.checkStatus`; widen typed bindings to `bool?`.

### 5. Build-time guard

Not a wire change. A banned-symbols rule forbidding direct `ArgumentException` and `ArgumentOutOfRangeException` construction (the public-message carriers excepted) ships in 5.0 (`argument-exception-public-message-guard`, `src/BannedSymbols.txt`): production code that constructs either type fails the build with RS0030 and must use `PublicArgumentException`, `PublicArgumentOutOfRangeException` or `ArgumentErrors.Redacted`.

## Consequences

- Every breaking item above has a `Changed — BREAKING` fragment with a `Migration:` sentence; the 5.0.0 changelog section is generated from them.
- ADR 0001 section 2 is amended by section 2 of this ADR.
- `docs/product-contract.md` "Deprecations scheduled for 5.0" is replaced by the 5.0 contract.
- Future tool consolidation (`tool-merge-*` rows) needs its own ADR and alias lifecycle.
