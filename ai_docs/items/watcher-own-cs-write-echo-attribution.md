# watcher-own-cs-write-echo-attribution — Attribute the server's own .cs/.csproj apply writes so their watcher echo is not ExternalEdit

**row:** `watcher-own-cs-write-echo-attribution` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/FileWatcherService.cs`
- `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs`
- `tests/RoslynMcp.Tests/ExternalEditStalenessTests.cs`

## Acceptance

- [ ] A delayed OS echo of the server's own `.cs`/`.csproj` write outside a reload is attributed Apply, not ExternalEdit, by committed-bytes ownership like the existing `.editorconfig` `_ownedConfigBytes` path; a genuine external edit to the same path with different bytes stays ExternalEdit.
- [ ] ExternalEdit is sticky within a stale window: a later Apply mark never overwrites an earlier ExternalEdit reason (MarkStaleWithReason is last-writer-wins; EnsureFreshForWritePreview can read "apply" while an external edit is pending).
- [ ] FileWatcherService.cs class remarks describe the `.cs` attribution rule.
- Out of scope (done): the mid-reload echo window is closed by PR 2050 (time-based ClearStaleIfNoExternalEditSince; an echo carries the older write time and clears).

## Evidence

- Cold review of the epoch fix (reload-clearstale-swallows-midreload-watcher-event): MarkFileChange (FileWatcherService.cs ~577-603) marks every non-.editorconfig event ExternalEdit; with the epoch fix a delayed echo of the server's own apply landing mid-reload now survives the post-load clear and keeps the workspace stale (external-edit) until one more reload. Safe and bounded (write-preview tools refuse until reload; AutoReload reloads once; a reload itself writes nothing watched, so no loop), but spurious.

## Context

- Same-mechanism family as the epoch fix: stale attribution at the watcher. Different mechanism from the post-load clear; ships separately.
