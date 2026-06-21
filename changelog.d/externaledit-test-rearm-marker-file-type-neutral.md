---
category: Maintenance
---

- **Maintenance:** Hardened the dropped-event re-arm in `ExternalEditStalenessTests.WaitForStaleAsync` to append a **file-type-neutral** marker (trailing whitespace, unique per attempt) instead of a C#-comment marker (`// watcher re-arm {guid}`). The helper accepts an arbitrary tracked-file path; the comment syntax was invalid markup had it ever been invoked with a `.csproj`/`.props`/`.targets`/`.sln` file. Trailing whitespace is ignored by C#, MSBuild XML, and sln parsers while still strictly growing the file each attempt, so the watcher re-arm is preserved. Closes `externaledit-test-rearm-marker-file-type-neutral`.
