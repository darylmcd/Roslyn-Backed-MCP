# ci-flaky-fswatcher-staleness-test — stabilize the flaky FileSystemWatcher staleness test

**row:** `ci-flaky-fswatcher-staleness-test` · **pri:** `High` · **size:** `S` <!-- cache — backlog row is canonical for pri/size -->

## Anchors

- `tests/RoslynMcp.Tests/ExternalEditStalenessTests.cs:112` (test), `:130`, `:295` (`WaitForStaleAsync`)
- `eng/verify-release.ps1:82` (the `dotnet test` step that runs it)
- `.github/workflows/publish-nuget.yml:36` (release path that runs `verify-release.ps1` on ubuntu)

## Acceptance

- [ ] `EnsureFreshForWritePreview_RefusesWithReloadHint_WhenExternalEdit` no longer fails intermittently under runner load — drive it off the watcher's actual event (or a bounded poll until the stale flag flips) instead of a fixed 2000 ms wall-clock window; OR quarantine with an explicit retry so a single dropped OS event doesn't fail the run.
- [ ] No wall-clock-only assertion remains in `WaitForStaleAsync`.

## Evidence

- Flaked on PR #975 (`validate`, 2026-06-19): `Assert.Fail ... FileSystemWatcher did not flip isStale within 2000 ms ... or the OS dropped the event.` 1560/1561 passed; re-run passed with zero code change. Runner log showed contention (4 orphaned `dotnet` processes). The packed README itself documents FileSystemWatcher unreliability as a known trait.

## Context

Risk escalated by the 2026-06-19 registry-publish work: `verify-release.ps1` (which runs this test) now executes in `publish-nuget.yml` on **ubuntu** as part of the release, so a flake here can fail the NuGet + MCP-registry publish, not just a PR. Linux runs the watcher on inotify (different timing) and PR CI only exercises Windows (self-hosted) — the per-PR Linux gap is the related `repo`-level concern; an OS matrix on PR CI would also surface this earlier.
