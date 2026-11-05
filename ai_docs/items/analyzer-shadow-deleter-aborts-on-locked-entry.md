# analyzer-shadow-deleter-aborts-on-locked-entry — NoFollowDirectoryDeleter aborts the walk at the first locked entry

**row:** `analyzer-shadow-deleter-aborts-on-locked-entry` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Helpers/NoFollowDirectoryDeleter.cs`

## Acceptance

- [ ] A locked sibling does not stop deletion of the remaining entries; the aggregate failure names the locked entries; red regression first.

## Evidence

- NoFollowDirectoryDeleter.cs ~221; PR 1996 had to order test file names to keep its red case honest.
