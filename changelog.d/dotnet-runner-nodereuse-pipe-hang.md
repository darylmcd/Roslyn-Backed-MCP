---
category: Fixed
---

- **Fixed:** `DotnetCommandRunner` no longer hangs after a spawned `dotnet` command exits while MSBuild reusable worker nodes or VBCSCompiler still hold the inherited stdout/stderr pipe handles (they idle up to 15 minutes, and continuous reuse resets the timer — on CI this burned the full command timeout of every `build_workspace`/`test_run`/vulnerability-scan invocation that spawned fresh nodes, cascading into job-timeout kills). Spawned commands now run with `MSBUILDDISABLENODEREUSE=1` (one-shot commands gain nothing from node reuse, and idle nodes no longer accumulate on the host), and the post-exit stream drain is bounded by a 5-second grace window instead of waiting for a pipe EOF that only arrives when every handle-inheriting descendant dies. This is the root cause behind the CI timeout band-aids (vulnerability-scan timeout 2→5 min, CI job timeout 25→40 min).
