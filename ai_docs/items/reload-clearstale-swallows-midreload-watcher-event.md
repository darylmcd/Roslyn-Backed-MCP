# reload-clearstale-swallows-midreload-watcher-event — Do not clear staleness set during reload

**row:** `reload-clearstale-swallows-midreload-watcher-event` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs:613`

## Acceptance

- [ ] A file edit observed during reload leaves the workspace stale; deterministic test with a controllable watcher.

## Evidence

- Pre-existing, seen while planning PR 1909. PR 1909 collapses concurrent stale callers under the writer, which makes the lost-event window the remaining staleness gap.
- Sighted during the 2026-10-29 remediation of refactoring-rebase-source-kind-preservation, cross-project-interface-reference-binding and automatic-reload-analyzer-reader-lifetime (PRs 1908-1910). Not re-derived beyond the cold reviews.
