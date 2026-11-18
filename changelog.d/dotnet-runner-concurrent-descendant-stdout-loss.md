---
category: Fixed
---
- **Fixed:** `build_workspace`, `test_run` and other `dotnet` child commands no longer lose output the child already wrote when a descendant process (an MSBuild worker node, VBCSCompiler) keeps the output pipe open. The runner now reads only bytes already in the pipe and drains it after the child exits, instead of cancelling a pending read when the post-exit grace period expires; that cancellation could discard a short final output entirely when the read had not run yet. Output capture no longer parks a thread-pool worker per stream on Windows.
