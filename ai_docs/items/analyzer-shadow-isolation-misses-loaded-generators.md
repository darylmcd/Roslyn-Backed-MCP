# analyzer-shadow-isolation-misses-loaded-generators - Release analyzer/generator DLL locks on workspace_close

**row:** `analyzer-shadow-isolation-misses-loaded-generators` · **pri:** `High` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Helpers/AnalyzerReferenceIsolation.cs`
- `tests/RoslynMcp.Tests/WorkspaceCloseDrainTests.cs`

## Acceptance

- [ ] Analyzer and generator references outside the workspace roots are shadow-copied before load instead of loading from their original path, which `AnalyzerReferenceIsolation.cs:95` does today. The external copies load non-collectibly, so the #1402 (`92ac5a9c`) access-violation mitigation still holds, and the file lock lands on the shadow copy.
- [ ] External shadow copies are shared process-wide (one copy per source path and file identity) in a process-scoped directory that the abandoned-root sweep reclaims after the host exits, so repeated loads do not pile up shadow trees that per-lease disposal can no longer delete.
- [ ] Red-first test: load a workspace whose packages carry source generators from a package folder outside the workspace roots, run a generator-exercising operation, `workspace_close`, then delete the package folder. It fails at `19ccd61b` and passes after the fix.
- [ ] `workspace_close` (with or without `drainProcesses`) leaves no module in the host process mapped from the original package folder.
- [ ] backlog: sync ai_docs/backlog.md

## Evidence

- Observed 2026-09-27 on roslyn-mcp 4.2.1. An executor loaded a consumer solution from a linked worktree with `NUGET_PACKAGES` pointing at a per-worktree scratch package folder, then closed it; `workspace_list` showed only the unrelated primary workspace. The host process still mapped 6 DLLs from that package folder, including `Microsoft.Extensions.Logging.Generators.dll`, `Microsoft.Extensions.Options.SourceGeneration.dll`, `Microsoft.Gen.Logging.dll`, `Microsoft.Gen.Metrics.dll`, `StackExchange.Redis.Build.dll` and `Microsoft.AspNetCore.OpenApi.SourceGenerators.dll`. Because they load from the original path and not from the `%TEMP%/RoslynMcpAnalyzerShadow` lease, `AnalyzerShadowLoaderLease` disposal cannot release them.
- Impact: `bsweep-state land` teardown failed EPERM deleting the scratch folder and refused to merge (TradeWise plan 20260927T021440Z, PR #2658 landed via the fallback path); the folder stays locked until the server restarts.

## Context

`AnalyzerReferenceIsolation.cs:36-45` documents the lease as reclaiming load contexts on workspace close; this is a hole in that contract, not a new feature.

## Notes

- 2026-09-28 re-derivation (retro 2026-09-27, `analyzer-dll-lock-survives-workspace-close`): the cause is the external-analyzer skip at `AnalyzerReferenceIsolation.cs:95` (added by #1402, `92ac5a9c`), not a pre-populated `_lazyAssembly` or a generator-driver loader. Raised to High: the lock blocked landing teardown in 6 sessions, which led to killing the Roslyn host and dropping the tool for the rest of a run.
- Companions in the global agent tooling (reviewer workspace close, land-teardown coupling) are outside this repo's scope.
