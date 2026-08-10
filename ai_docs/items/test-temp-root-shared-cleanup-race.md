# test-temp-root-shared-cleanup-race — per-run temp root so concurrent test assemblies stop deleting each other's fixtures

**row:** `test-temp-root-shared-cleanup-race` · **pri:** `Medium` · **size:** `S`

## Anchors

- `tests/RoslynMcp.Tests/AssemblyCleanup.cs:14` (`tempRoot` = shared parent)
- `tests/RoslynMcp.Tests/AssemblyCleanup.cs:16-25` (`Directory.Delete(tempRoot, recursive: true)`)
- `ai_docs/prompts/backlog-sweep-addenda.md` (no `parallel_safety` declaration)
- `ai_docs/known-flakes.md` (both registered flakes blamed on "runner load")

## Acceptance

- [ ] The test temp root is per-run — `Path.Combine(Path.GetTempPath(), "RoslynMcpTests", <run-scoped id>)` — so `[AssemblyCleanup]` deletes ONLY its own subtree and can never remove a concurrent run's in-flight fixtures. Two test-assembly runs started concurrently both complete without `DirectoryNotFoundException`.
- [ ] `ai_docs/prompts/backlog-sweep-addenda.md` declares `parallel_safety.parallelSafe` explicitly — `false` while the shared root persists (which forces `/backlog-sweep:execute` serial mode + the `ci-lock-acquire`/`ci-lock-release` wrap), or `true` with a one-line rationale once the per-run isolation above lands.
- [ ] The two entries in `ai_docs/known-flakes.md` are re-triaged against this race rather than "self-hosted CI runner load", and the registry's dangling citation of `filewatcher-clearstale-timeout-flake-triage` — a row absent from `ai_docs/backlog.md` while the entry claims it "stays open" — is either re-filed or corrected.

## Evidence

- Reproduced during backlog-sweep `20260810T175048Z`: a targeted `dotnet test` in one worktree returned `Failed: 6, Passed: 68` where all 6 failures were `System.IO.DirectoryNotFoundException` on paths under `C:\Users\daryl\AppData\Local\Temp\RoslynMcpTests\<guid>\...` (`UndoFileOperationsTests`, `EditorConfigServiceTests`, `EditUndoIntegrationTests`, `ProjectMutationIntegrationTests`, `ApplyTextEditVerifyTests`, `UndoIntegrationTests`), thrown at fixture-write time before any production code under test ran. 23 `dotnet`/`testhost` processes were live (self-hosted CI runner + two sweep worktrees).

## Context

`AssemblyCleanup.Cleanup()` computes `tempRoot` as the **shared parent** `%TEMP%\RoslynMcpTests` — not a per-run subdirectory — and deletes it recursively. Every test assembly writes its fixtures into `%TEMP%\RoslynMcpTests\<guid>\`, so whichever assembly finishes FIRST wipes the fixture trees of every other run still in flight.

The existing `catch { }` acknowledges the hazard ("another test runner instance may hold a lock") but only protects the **deleter** from throwing; it does nothing for the victim, which sees its fixture directory vanish mid-test.

Consequences:

| Impact | Detail |
|---|---|
| False reds | Concurrent local + CI runs produce fixture-not-found failures unrelated to the diff. |
| Flake mis-attribution | Both `known-flakes.md` entries are attributed to "runner load"; this race is a competing explanation that was never considered. |
| Silent parallel-sweep hazard | This repo's addenda declares no `parallel_safety`, so `/backlog-sweep:execute` picks parallel mode and races its own validation runs. |

The fix is isolation (per-run root), not serialization — serialization via `parallelSafe: false` is the interim workaround the addenda should state until isolation lands.

## Notes

- Per-run id must be stable for the whole assembly lifetime and unique per process; the process id alone is insufficient if two runs on one box reuse a pid across time, so combine pid + a start timestamp or a single static `Guid`.
- `HostProcessMetadataTests.cs:42`, `RestoreStalenessDetectorTests.cs:25`, `ServiceCollectionExtensionsTests.cs:155`, `Services/PersistentCompositeStorageTests.cs:27`, `Skills/AggregatePromotionScorecardsScriptTests.cs:30`, `MsBuildEvaluationCacheTests.cs:150` and `ProjectOutputTypeTests.cs:203` all build paths under the same shared root — they inherit the isolation automatically if the root is centralized, so prefer one shared helper over editing each site.
