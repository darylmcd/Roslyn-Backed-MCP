---
category: Fixed
---

- **Fixed:** Concurrent test-assembly runs no longer destroy each other's fixtures. `[AssemblyCleanup]` deleted the *shared* `%TEMP%/RoslynMcpTests` parent recursively, so whichever run finished first wiped every other run's in-flight fixture tree — surfacing as `DirectoryNotFoundException` at fixture-write time in tests unrelated to the change under test. Every temp path now lives under a per-process root (`TestTempRoot.Current` = `%TEMP%/RoslynMcpTests/run-<pid>-<rand>/`) and cleanup deletes only that subtree, with an age-gated reaper for roots abandoned by crashed hosts. Verified by two concurrent `dotnet test` runs over the fixture-heavy undo/edit/project-mutation set: 74 passed each, 0 `DirectoryNotFoundException`, where the same command previously failed 6. Test-only change; `ai_docs/prompts/backlog-sweep-addenda.md` now declares `parallel_safety.parallelSafe: true` with that evidence. Closes `test-temp-root-shared-cleanup-race`.
