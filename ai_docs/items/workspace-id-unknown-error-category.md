# workspace-id-unknown-error-category — Give an unknown workspaceId its own error, not the generic NotFound

**row:** `workspace-id-unknown-error-category` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Core/Services/WorkspaceNotFoundException.cs`
- `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs`
- `src/RoslynMcp.Roslyn/Services/WorkspaceExecutionGate.cs`
- `src/RoslynMcp.Host.Stdio/Tools/ToolErrorHandler.cs`
- `tests/RoslynMcp.Tests/WorkspaceManagerEvictionTests.cs`

## Acceptance

- [ ] A new `WorkspaceNotFoundException : KeyNotFoundException` is defined in `src/RoslynMcp.Core/Services/WorkspaceNotFoundException.cs`, mirroring `WorkspaceEvictedException` (same namespace, same derive-from-`KeyNotFoundException` rule so existing catch sites — including `ToolDispatch.ReadByWorkspaceIdWithEvictionRetryAsync`'s `catch (KeyNotFoundException)` — still observe a lookup miss).
- [ ] `WorkspaceManager.cs:1403` throws that type instead of the bare `KeyNotFoundException`, carrying the same `workspaceId` and active-session count the raw message already builds.
- [ ] `WorkspaceExecutionGate.cs:207` (the `ContainsWorkspace` precheck) no longer throws a bare `KeyNotFoundException`: it routes the miss through the manager's three-way classification so the caller gets `WorkspaceEvicted` or the new category, never the generic one. `:524` (the post-lock recheck) gets the same treatment. This is the site the retro's failures actually hit — `ToolDispatch.cs:501-506` documents that the precheck "never reach[es] `WorkspaceManager.GetRequiredSession`'s richer classification", and ~83 of 85 `ReadByWorkspaceIdAsync` call sites have no eviction re-probe to compensate.
- [ ] `ToolErrorHandler.cs` gains a `ToolErrorCategory` member (enum at `:42`, constant in `ErrorCategories` at `:74`) and a handler entry registered BEFORE the `[typeof(KeyNotFoundException)]` entry at `:119` — the same insertion-order rule the `WorkspaceEvictedException` entry at `:105` already relies on — so the more specific category wins the dictionary walk.
- [ ] `ResourceReadResultFilter.MapErrorCode` gets an explicit switch arm for the new category (caller fault vs server fault), as the `ErrorCategories` XML doc at `:65-73` requires of every added category.
- [ ] The public message states the active-session count and names the `workspace_list` → `workspace_load` recovery; it does not leak the loaded path.
- [ ] `BuildSafeNotFoundMessage` (`ToolErrorHandler.cs:559`) is left handling only symbol, document and handle misses — its generic "Ensure the workspace is loaded and the identifier is correct" text no longer fires for an unknown workspaceId.
- [ ] The two existing tests that pin the bare type are updated, not deleted: `NeverLoaded_AndNoRecycle_Throws_PlainKeyNotFoundException` (`WorkspaceManagerEvictionTests.cs:254`) and `HostRecycled_ButLiveSessionExists_TypoStillThrowsKeyNotFoundException` (`:280`) both use `Assert.ThrowsExactly<KeyNotFoundException>` and will fail against a derived type; the class XML doc's path-3 description (`:39-41`) is amended in the same edit.
- [ ] A test pins all four shapes as distinguishable from the envelope alone, driven through a GATED tool call (not a direct manager call) so it exercises the precheck path: unknown/never-loaded workspaceId → the new category; evicted workspaceId → still `WorkspaceEvicted`; bad `metadataName` → still `NotFound`; stale member `symbolHandle` → still `NotFound`.
- [ ] This changes the published wire contract on a path that previously returned `NotFound`, so the change ships with a `changelog.d/` fragment carrying an explicit migration note for consumers branching on `category`. It also supersedes CHANGELOG.md:927's "Typoed-`workspaceId` lookups remain `category=NotFound`".

## Evidence

An unknown or evicted-by-another-process workspaceId returns the same NotFound envelope and `exceptionType: KeyNotFoundException` as a wrong `metadataName` or a stale `symbolHandle`, so agents cannot tell "reload the workspace" from "no such symbol" — 131 failed calls across 3 sessions in both harnesses, with 19 of 76 scorers in one session abandoning the server for grep.

- Report: `ai_docs/reports/20260913T200750Z_roslyn-backed-mcp_roslyn-mcp-multisession-retro.md`
- Finding id: `unknown-workspace-id-error-category` (section 4.1)
- Upstream evidence ids: `2a#multi-tool-workspace-id-notfound`, `2a#find_references-symbolhandle-keynotfound`, `2a#find_references-metadataname-symbolnotfound`, `2b#workspace-already-loaded-dead-id`, `3#workspace-handle-lost-across-agent-boundary`

## Context

Live-source check (2026-09-13, `main` @ 985da64c). The retro's cited line numbers have drifted — the generic manager throw is at `WorkspaceManager.cs:1403` (retro said `:1367`) — and, more importantly, the retro named only one of two throw sites.

**Two throw sites, and the manager's is the minority path.** `WorkspaceExecutionGate.ExecuteAsync` runs a cheap `ContainsWorkspace` precheck at `:207` and throws a bare `KeyNotFoundException` before any tool body executes; `:524` repeats it after the lock is acquired. Because every gated tool goes through this path, an unknown workspaceId short-circuits there and the manager's richer classification at `WorkspaceManager.cs:1355-1408` is never reached. `ToolDispatch.cs:495-512` states this in prose and works around it for exactly two tools (`compile_check`, `test_run`) via `TryReclassifyAsEvicted` (`:580`) — 4 call sites of `ReadByWorkspaceIdWithEvictionRetryAsync` against 85 plain `ReadByWorkspaceIdAsync` sites. Anchoring only the manager would ship a change that leaves the retro's own reproducer (Q1a `find_references`, Q1b `compile_check`) unchanged.

The `WorkspaceEvicted` precedent (`mcp-error-category-workspace-evicted-on-host-recycle`) carved eviction out of the manager's throw site and inherited the same gate blind spot; fixing the gate here closes that gap for both categories at once. The code comment at `WorkspaceManager.cs:1370-1377` explicitly calls out arm 3 ("genuinely a typo or a never-loaded id") as falling through to the generic path.

Sizing is at the production cap: 4 production anchors (new exception type, manager throw site, gate precheck, ToolErrorHandler registration + enum member + category constant, all three of which live in the single `ToolErrorHandler.cs`) plus 1 test file. Do not fold anything else in — the `ToolErrorHandler` insertion-order-as-correctness hardening noted below needs its own row.

Couplings the implementer must not miss:

- `items/compile-check-eviction-retry-nonrecovering-coverage.md` asserts the bogus-id shape leaves "the pre-existing NotFound error envelope unchanged". That becomes false when this row lands and must be updated in the same PR.
- `WorkspaceManagerEvictionTests.cs:254` and `:280` use `Assert.ThrowsExactly<KeyNotFoundException>` and will fail against the derived type (see Acceptance).
- Published wire contract on `roslyn-mcp@roslyn-mcp-marketplace` → contract-care mode per Directive #4; the migration note rides in the changelog fragment.

Bad code surfaced while verifying (Directive #3): `ToolErrorHandler`'s dispatch relies on `Dictionary` insertion order for correctness — documented only in the comments at `:100-110` and `:395-400`, enforced by nothing. A reordering edit would silently regress the `WorkspaceEvicted` category with no test failing. This deserves its own row (an exception-hierarchy walk, or a test that pins registration order); it does not fit inside this row's production-anchor budget.
