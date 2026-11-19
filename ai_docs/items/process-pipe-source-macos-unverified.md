# process-pipe-source-macos-unverified — Run the Unix process pipe source on macOS

**row:** `process-pipe-source-macos-unverified` · **pri:** `Low` · **size:** `S` · **verified:** `b1d2a4b22f4e1105f98a08ff452be5a6c9562d24`

## Anchors

- `src/RoslynMcp.Roslyn/Helpers/ProcessPipeSource.cs` — `UnixPipeSource`: `poll` and `read` through `libc`, with `POLLIN` and the Linux and macOS `EAGAIN` values hard-coded.
- `tests/RoslynMcp.Tests/DotnetCommandRunnerPipeLifetimeTests.cs` — `PipeSource_*` and `RunAsync_ConcurrentOwnedDescendants_HoldPipesWithoutDisruptingEachOtherAsync`.

## Acceptance

- [ ] The `PipeSource_*` tests and the concurrent-descendant test are run on macOS and their result is recorded, or a macOS leg is added to hosted CI that runs them.
- [ ] If any fails, the source is corrected and the row states what differed from Linux.

## Evidence

- PR 2158 (2026-11-18) replaced output capture for every `dotnet` child command with this source. Hosted CI ran it on Windows and on `linux-5-of-5`, where the tests named above passed. Hosted CI has no macOS leg and no macOS machine was available.
- The code assumes `struct pollfd` layout, `POLLIN = 0x1`, `nfds_t` passed as a native-width integer, and `EAGAIN = 35` on macOS. These are standard values but were not executed there.
