# compilation-cache-wire-group-c-consumer — opt a real production caller into the group-c cache params and re-establish the remaining-site inventory

**row:** `compilation-cache-wire-group-c-consumer` · **pri:** `Medium` · **size:** `M` · **deps:** `group-c-compilation-cache-gate-hardening`

## Anchors

- `src/RoslynMcp.Roslyn/Helpers/SymbolResolver.cs:51-68` (the liveness gate)
- `src/RoslynMcp.Roslyn/Helpers/SymbolResolver.cs:100`, `:144` (internal callers, 3-arg form)
- `src/RoslynMcp.Roslyn/Helpers/SymbolHandleSerializer.cs:55` (3-arg form)
- `src/RoslynMcp.Host.Stdio/Tools/SymbolTools.cs:911` (tool-surface caller, 3-arg form)
- `tests/RoslynMcp.Tests/CompilationCacheAdoptionTests.cs`

## Acceptance

- [ ] At least ONE production call site passes `compilationCache` + `workspaceId` into `SymbolResolver.ResolveByMetadataNameAsync` / `FindClosestMatchesAsync` / `SymbolHandleSerializer`, so the group-c cache path is actually reachable at runtime. A recording-`ICompilationCache` test asserts the cache is invoked through that real call path (not just through a direct helper call).
- [ ] `CrossProjectRefactoringService.cs:236` is NOT opted in (it passes a forked solution), and a test asserts the forked path still falls through to `project.GetCompilationAsync`.
- [ ] The remaining raw-`GetCompilationAsync` inventory is re-established: enumerate the ~22 `.GetCompilationAsync(` sites still in `src/`, classify each live-vs-forked, and record the result here (this inventory was lost when `compilation-cache-adoption-read-side` closed). Confirmed forked exclusions stay raw: `InterfaceExtractionService:436`, `RefactoringService:415`, `TypeMoveService:217`.

## Evidence

- Cold review of backlog-sweep `20260810T175048Z` (2026-08-10): PR #1207 added `ICompilationCache? compilationCache = null, string? workspaceId = null` to the three group-c helpers, but **every** call site in `src/` passes the 3-argument form — `SymbolTools.cs:911`, `SymbolHandleSerializer.cs:55`, `SymbolResolver.cs:100`, `:144`, `CrossProjectRefactoringService.cs:236`, `RecordFieldAdditionService.cs:124`, `SymbolRefactorService.cs:972`. The cache path is therefore unreachable in production, so the row named *adoption* closed with zero adoption.

## Context

`compilation-cache-adoption-read-side` was closed by PR #1207 in sweep `20260810T175048Z`. The implementation was correct and in-budget — the helpers are `static` with no `workspaceId`, so threading a REQUIRED parameter would have touched 6+ production files and blown Rule 3's 4-file cap. Optional parameters were the right call for that PR.

The mistake was **closing the row** on that basis. The sibling initiative in the same sweep (`shipped-skills-hardcode-bare-roslyn-tool-prefix`) faced the identical shape — a correct bounded batch that did not finish the row — and correctly emitted `rowsClosed: []`, leaving the row open with an amendment and a priority bump. This row exists to restore the tracking that inconsistency dropped.

Closing also deleted `ai_docs/items/compilation-cache-adoption-read-side.md`, which carried:

| Lost content | Why it mattered |
|---|---|
| ~48-site raw `GetCompilationAsync` survey across ~30 files | The only record that the `X/24` accounting was approximate |
| `ScaffoldingService` anchor retraction (2026-08-06) | Prevents re-scoping work against an anchor that never matched |
| "Re-scope this row before driving the remainder" instruction | The doc's own staleness warning |

Note one piece of good news the sweep never recorded: the group-b tail that the closed row still listed as open is in fact resolved at HEAD — `InterfaceExtractionService.cs:407` already routes through `_compilationCache`, and `:436` is documented at `:429` as deliberately raw (forked). So the row's "remaining scope" text was itself stale.

## Notes

- Sequence AFTER `group-c-compilation-cache-gate-hardening`: that row fixes the gate's mid-scan liveness race and the cross-workspace hole. Wiring a consumer before the gate is hardened would make a currently-unreachable defect reachable.
- Do NOT widen the helpers' signatures to required parameters without splitting — the 6-caller fanout is what forced the optional shape in the first place.
