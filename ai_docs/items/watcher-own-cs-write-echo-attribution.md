# watcher-own-cs-write-echo-attribution — Attribute the server's own .cs/.csproj apply writes so their watcher echo is not ExternalEdit

**row:** `watcher-own-cs-write-echo-attribution` · **pri:** `Medium` · **size:** `M` · **verified:** `c6c1265b67971daa51ebbc606f5a69a7f692fbb2`

## Anchors

- `src/RoslynMcp.Roslyn/Services/FileWatcherService.cs`
- `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs`
- `tests/RoslynMcp.Tests/ExternalEditStalenessTests.cs`

## Acceptance

- [ ] A delayed OS echo of the server's own `.cs`/`.csproj` write (TryApplyChanges) is attributed Apply, not ExternalEdit, by committed-bytes ownership like the existing `.editorconfig` `_ownedConfigBytes` path; a genuine external edit to the same path with different bytes stays ExternalEdit.
- [ ] Regression: an echo landing mid-reload (after the epoch read in ReloadAsync) leaves the workspace non-stale after the reload; a real external edit mid-reload still survives (test from PR for reload-clearstale-swallows-midreload-watcher-event stays green).
- [ ] ExternalEdit is sticky within a stale window: a later Apply mark never overwrites an earlier ExternalEdit reason (MarkStaleWithReason is last-writer-wins today; EnsureFreshForWritePreview can read "apply" while an external edit is pending).
- [ ] FileWatcherService.cs class remarks and the ReloadAsync comment that documents the limitation cite this row or are removed.

## Evidence

- Cold review of the epoch fix (reload-clearstale-swallows-midreload-watcher-event): MarkFileChange (FileWatcherService.cs ~577-603) marks every non-.editorconfig event ExternalEdit; with the epoch fix a delayed echo of the server's own apply landing mid-reload now survives the post-load clear and keeps the workspace stale (external-edit) until one more reload. Safe and bounded (write-preview tools refuse until reload; AutoReload reloads once; a reload itself writes nothing watched, so no loop), but spurious.

## Context

- Same-mechanism family as the epoch fix: stale attribution at the watcher. Different mechanism from the post-load clear; ships separately.
