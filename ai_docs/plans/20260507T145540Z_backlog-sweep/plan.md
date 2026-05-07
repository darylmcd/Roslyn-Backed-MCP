# Backlog sweep plan — 20260507T145540Z

**Generated:** 2026-05-07T14:55:40Z
**Backlog snapshot:** 2026-05-07T18:30:00Z
**Initiative count:** 1
**Anchor verification:** performed
**Scope:** single row `workspace-id-recovery-hints` (caller-restricted sweep)

## Initiatives (in order)

### 1. workspace-id-recovery-hints

| Field | Content |
|---|---|
| Status | in-review (branch: remediation/workspace-id-recovery-hints) |
| Backlog rows closed | `workspace-id-recovery-hints` |
| Diagnosis | PR #468 shipped `WorkspaceEvictedException` carrying `WorkspaceId`, `ServerStartedAtUtc`, and `WorkspaceLoadedAtUtc`. The structured envelope mentions in prose that the caller should "Call `workspace_load` with the original solution path to rehydrate," but the path itself is **not** carried as a structured field — see `src/RoslynMcp.Core/Services/WorkspaceEvictedException.cs:60-77` (no `LoadedPath` property), `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs:70` (`_evictedWorkspaces` value type is `DateTimeOffset`, no path), and `src/RoslynMcp.Host.Stdio/Tools/ToolErrorHandler.cs:34-42` (envelope formatter emits `serverStartedAt` + `workspaceLoadedAt` only). At the same-process eviction site (`WorkspaceManager.cs:1913-1923`) the manager has `evictedLoadedAt` but no path; at the cross-process recycle site (`WorkspaceManager.cs:1925-1935`) neither is recoverable. Eviction is recorded via `RecordEviction` at lines 274 and 673 — both call sites have a live `WorkspaceSession` in scope and therefore have access to `session.LoadedPath`. The gate at `src/RoslynMcp.Roslyn/Services/WorkspaceExecutionGate.cs:155-158` throws bare `KeyNotFoundException` ("typoed-id remains `NotFound`") and must remain unchanged so the typo path is preserved. |
| Approach | (a) Extend `WorkspaceEvictedException` with a nullable `LoadedPath` property and a new ctor parameter; the cross-process recycle ctor passes `null`. (b) Change `WorkspaceManager._evictedWorkspaces` from `ConcurrentDictionary<string, DateTimeOffset>` to `ConcurrentDictionary<string, EvictedSessionRecord>` where `EvictedSessionRecord` is a private `readonly record struct (DateTimeOffset LoadedAtUtc, string LoadedPath)`. Update `RecordEviction` signature (now takes loadedPath), the two call sites at lines 274 and 673 (both pass `session.LoadedPath`), and the same-process throw at lines 1913-1923 (passes `record.LoadedPath` to the new ctor parameter). The bounded-eviction trim loop in `RecordEviction` is unaffected — it operates on keys, not values. (c) Update `ToolErrorHandler.cs` `WorkspaceEvictedException` formatter (lines 34-42) to append `loadedPath=<path>; recovery=workspace_load(path: "<path>")` when `LoadedPath` is non-null, and to fall back to the existing message when null (cross-process recycle). The structured fields go in the envelope's `details` segment in the same `key=value;` style already used for `serverStartedAt` and `workspaceLoadedAt`. (d) Gate path at `WorkspaceExecutionGate.cs:155-158` is **not modified** — bare `KeyNotFoundException` continues to surface as `NotFound` for typoed ids. |
| Scope | Production files: 3 — `src/RoslynMcp.Core/Services/WorkspaceEvictedException.cs`, `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs`, `src/RoslynMcp.Host.Stdio/Tools/ToolErrorHandler.cs`. Test files: 1 — extend `tests/RoslynMcp.Tests/WorkspaceManagerEvictionTests.cs`. NOT tool-surface-only (touches Core + Roslyn types, not just envelope) — standard fix/refactor cap (≤4 prod files) applies and is satisfied. `WorkspaceManager.cs` is an addenda-listed hotspot; single-initiative wave so no parallel-wave conflict. |
| Tool policy | `edit-only` |
| Estimated context cost | 45000 |
| Risks | (1) `_evictedWorkspaces` value-type change ripples to any direct readers — verify via `find_references` on `_evictedWorkspaces` before editing. Field is `private`, so blast radius is intra-class. (2) `WorkspaceEvictedException` ctor signature change is breaking for any caller; field is `Core`-layer and only thrown from `WorkspaceManager` (verified: no other constructors located). (3) Tests asserting current envelope format (without `loadedPath`) must be updated; targeted at `WorkspaceManagerEvictionTests` and `ToolErrorHandlerWorkspaceReloadRaceTests`. (4) Cross-process recycle path correctly omits `loadedPath` (null) — assert via test that the envelope does not contain `loadedPath=` when the recycle ctor was used. (5) Path-quoting in the recovery hint: paths may contain spaces; the envelope must quote `"<path>"` so the agent can copy-paste. |
| Validation | (a) `mcp__roslyn__compile_check` after each file edit. (b) `mcp__roslyn__test_run --filter "WorkspaceManagerEvictionTests\|ToolErrorHandler"` covering: missing-id typo → `NotFound` (no `loadedPath`, no `recovery`); same-process eviction → `WorkspaceEvicted` with `loadedPath=<path>` and `recovery=workspace_load(path: "<path>")`; cross-process recycle → `WorkspaceEvicted` without `loadedPath`/`recovery` (those fields absent). (c) `./eng/verify-release.ps1 -Configuration Release` before merge. (d) Manually re-read the envelope field-order to confirm the new fields slot in cleanly with `serverStartedAt` and `workspaceLoadedAt`. |
| Performance review | N/A — error path; not on a hot loop. `_evictedWorkspaces` value-type change shifts a `DateTimeOffset` to a `(DateTimeOffset, string)` record struct; allocation pattern unchanged (already a value type), only payload size grows by one string reference (~8 bytes on x64). |
| CHANGELOG category | Changed |
| CHANGELOG entry (draft) | **Changed:** `WorkspaceEvicted` envelopes now carry the originally-loaded solution path and an exact `workspace_load(path: "...")` retry hint when the eviction was a same-process trim (path was retained). Cross-process recycle envelopes still omit the path because the prior process's session metadata is unrecoverable. Typoed-`workspaceId` lookups remain `category=NotFound`. Closes `workspace-id-recovery-hints`. |
| Backlog sync | Close rows: `workspace-id-recovery-hints`. Mark obsolete: none. Update related: none. |

---

## Self-vet checklist

- [x] No bundling — single row, single initiative.
- [x] ≤ 4 production files (3).
- [x] ≤ 3 test files (1).
- [x] `estimatedContextTokens` ≤ 80K (45K).
- [x] `toolPolicy` set explicitly (`edit-only`).
- [x] No two adjacent-`order` hotspot-touching initiatives — only one initiative.
- [x] No bracket-paren markdown links pointing into `src/` — all source citations use plain inline-code paths.
