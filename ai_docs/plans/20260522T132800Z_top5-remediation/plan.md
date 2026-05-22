# Top 5 Backlog Remediation Plan

<!-- scope: in-repo -->

Created: 2026-05-22T13:28:00Z

## Selection

Source: `ai_docs/backlog.md` as of `updated_at: 2026-05-22T07:35:36Z`.

Selection rule: inspect the five remaining canonical rows in priority order and
select only rows whose current deliverable is implementation-ready in this
session. Rows whose own wording requires future evidence, an external driver, or
future worse-profile data are classified but not forced into code changes.

Selected rows:

1. `workspace-cache-coordinator-extraction`
2. `initiative-executor-roslyn-tool-discovery-experiment`

Classified but skipped:

- `tool-surface-pagination-or-tool-sets` - externally blocked by its own
  requirement for fresh post-router evidence of small-model discovery friction.
- `http-streamable-host-project` - intentionally deferred pending a concrete
  remote-deployment driver with named users and an approved auth/observability
  plan.
- `workspace-process-pool-or-daemon` - intentionally deferred pending worse
  large-solution profile evidence.

## Plan

1. Add focused red tests around an internal `WorkspaceCacheCoordinator`:
   cache miss, stale metadata-reference rejection, matching graph hit, graph
   mismatch writeback, and fail-soft store exceptions.
2. Extract cache probe/writeback policy from `WorkspaceManager` into
   `src/RoslynMcp.Roslyn/Services/WorkspaceCacheCoordinator.cs`, preserving
   `IWorkspaceCacheStore`, `WorkspaceCacheStore`, public workspace contracts,
   cache key format, and `AmbientGateMetrics.CacheHit` semantics.
3. Keep `WorkspaceManager` responsible for load/session lifecycle, restore-race
   waiting, stale tracking, session replacement, and metrics orchestration.
4. Complete the initiative-executor measurement row by recording the current
   available sample source, semantic first-hop/generic tool/reload counts, and a
   go/no-go decision. If the sample is insufficient, close the row with a
   documented no-edit decision rather than editing `.claude/agents/`.
5. Add focused validation:
   - new coordinator test filter;
   - existing cache fast-path/store invalidation tests;
   - `compile_check`;
   - `./eng/verify-ai-docs.ps1`;
   - merge-ready `just ci`.
6. backlog: sync `ai_docs/backlog.md` by removing completed selected rows or
   replacing them with bounded follow-on rows only if the shipped evidence
   requires follow-up.
7. changelog: add a fragment covering the completed selected rows.

## Execution Notes

- `workspace-cache-coordinator-extraction`: implemented. Cache probing,
  graph/metadata hashing, metadata-reference freshness, and writeback moved to
  `WorkspaceCacheCoordinator`; `WorkspaceManager` now owns only load lifecycle
  orchestration and cache-hit metric stamping.
- `initiative-executor-roslyn-tool-discovery-experiment`: measured and closed
  as no-go for executor-brief edits. The only available refactoring-subagent
  sample predates `recommend_workflow`, so it confirms the pre-router problem
  but cannot justify a post-router `.claude/agents/initiative-executor.md`
  change. Measurement recorded in
  `ai_docs/items/initiative-executor-roslyn-tool-discovery-measurement.md`.
- `ai_docs/backlog.md` synced by removing both completed rows from open work.
- `changelog.d/workspace-cache-coordinator-extraction.md` added for the batch.
