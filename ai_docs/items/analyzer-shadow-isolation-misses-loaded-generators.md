# analyzer-shadow-isolation-misses-loaded-generators - Release analyzer/generator DLL locks on workspace_close

**row:** `analyzer-shadow-isolation-misses-loaded-generators` · **pri:** `Medium` · **size:** `M` · **deps:** `—`

## Anchors

- `src/RoslynMcp.Roslyn/Helpers/AnalyzerReferenceIsolation.cs`
- `tests/RoslynMcp.Tests/WorkspaceCloseDrainTests.cs`

## Acceptance

- [ ] Reproduce under a test: load a workspace whose packages carry source generators (e.g. Microsoft.Extensions.Logging.Abstractions `analyzers/dotnet/roslyn4.4/cs/Microsoft.Extensions.Logging.Generators.dll`) from a package root outside the shadow root, run a generator-exercising operation, `workspace_close`, then assert no module in the host process maps a file under the original package root and the root can be deleted
- [ ] Identify why these assemblies load from their original path despite `RetargetFileReferencesToShadowLoader` (candidates: `_lazyAssembly` already populated before retargeting, a generator driver path using Roslyn's default loader, or a skipped reference kind) and fix the root cause
- [ ] `workspace_close` (with or without `drainProcesses`) leaves the original analyzer paths unlocked
- [ ] backlog: sync ai_docs/backlog.md

## Evidence

- Observed 2026-09-27 on roslyn-mcp 4.2.1 (stdio pid 27460). After an executor loaded `C:/Code-Repo/TradeWise/.worktrees/bl-2752/TradeWise.sln` with `NUGET_PACKAGES=%TEMP%/bsweep-scratch/79a3c90a-bl-2752/nuget` and then closed it, `workspace_list` showed only the unrelated primary workspace. Yet `(Get-Process -Id 27460).Modules` still mapped 6 DLLs from that NUGET root, including `Microsoft.Extensions.Logging.Generators.dll`, `Microsoft.Extensions.Options.SourceGeneration.dll`, `Microsoft.Gen.Logging.dll`, `Microsoft.Gen.Metrics.dll`, `StackExchange.Redis.Build.dll` and `Microsoft.AspNetCore.OpenApi.SourceGenerators.dll`. Because they load from the original path and not from the `%TEMP%/RoslynMcpAnalyzerShadow` lease, `AnalyzerShadowLoaderLease` disposal cannot release them.
- Impact: `bsweep-state land` teardown failed EPERM deleting the scratch dir and refused to merge (TradeWise plan 20260927T021440Z, PR #2658 landed via the fallback path); the dir stays locked until the server restarts.

## Context

`AnalyzerReferenceIsolation.cs:36-45` documents the lease as reclaiming load contexts on workspace close; this is a hole in that contract, not a new feature.
