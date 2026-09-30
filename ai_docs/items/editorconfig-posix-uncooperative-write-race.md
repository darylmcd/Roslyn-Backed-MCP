# editorconfig-posix-uncooperative-write-race — Define external writer coordination on POSIX

**row:** `editorconfig-posix-uncooperative-write-race` · **pri:** `Defer` · **size:** `M` · **deps:** `editorconfig-owned-write-external-race`

## Anchors

- `src/RoslynMcp.Roslyn/Services/FileWatcherService.cs:41-100`
- `src/RoslynMcp.Roslyn/Services/FileWatcherService.cs:386-438`

## Acceptance

- [ ] Establish an enforceable cross-process coordination contract for uncooperative POSIX writers or a verified atomic conflict protocol
- [ ] A writer that ignores advisory locks cannot be silently overwritten between held-inode recheck and server write
- [ ] Linux regression proves the conflict outcome with an uncooperative writer

## Evidence

- PR #1678 Linux test proved that FileShare.Read does not exclude a second FileStream writer on POSIX. A held-inode recheck plus advisory lock detects cooperating or already-completed edits, but an external process that ignores advisory locking can write in the interval after recheck and before the server write. POSIX advisory lock enforcement belongs to the external writer; the server cannot require it through FileShare alone. See the PR #1678 Linux container regression and hosted linux-2-of-2 failure at head e136999e.
