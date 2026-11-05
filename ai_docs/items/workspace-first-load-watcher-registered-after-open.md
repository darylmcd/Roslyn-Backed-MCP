# workspace-first-load-watcher-registered-after-open — Initial load registers the file watcher only after open

**row:** `workspace-first-load-watcher-registered-after-open` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs`

## Acceptance

- [ ] Reproduce or disprove with a deterministic test; if confirmed, register the watcher before open or reconcile after open.

## Evidence

- P2 planner: unverified, different mechanism from reload-clearstale-swallows-midreload-watcher-event.
