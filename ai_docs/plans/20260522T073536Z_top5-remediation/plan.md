# Top 5 Backlog Remediation Plan

<!-- scope: in-repo -->

Created: 2026-05-22T07:35:36Z

## Selection

Source: `ai_docs/backlog.md` as of `updated_at: 2026-05-22T04:53:49Z`.

Selection rule: take the highest-priority actionable rows in backlog order. Design-only
and measurement-only rows count when the current deliverable is concrete and can be
closed in this session. Rows that explicitly require fresh evidence before
implementation are classified but not forced into code changes.

Selected rows:

1. `formatter-host-stdio-whitespace-slice`
2. `validate-locator-preflight-measurement`
3. `workspace-manager-cache-store-extraction-design`
4. `scripting-service-runtime-state-extraction-design`
5. `initiative-executor-roslyn-tool-discovery-brief`

Classified but skipped:

- `tool-surface-pagination-or-tool-sets` - intentionally parked until fresh
  post-router evidence shows small-model discovery friction after
  `recommend_workflow`.
- `http-streamable-host-project` - intentionally deferred pending a concrete
  remote-deployment driver.
- `workspace-process-pool-or-daemon` - intentionally deferred pending worse
  large-solution profile evidence.

## Plan

1. Normalize only the selected Host.Stdio whitespace slice:
   `src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.cs`,
   `src/RoslynMcp.Host.Stdio/Tools/AnalysisTools.cs`, and
   `src/RoslynMcp.Host.Stdio/Tools/ApplyWithVerifyTool.cs`.
2. Write `ai_docs/items/validate-locator-preflight-measurement.md` with current
   post-PR #483 measurement, numerator/denominator, and the decision on the
   preflight idea.
3. Write `ai_docs/items/workspace-manager-cache-store-extraction-design.md`
   deciding the cache-policy extraction boundary and, if justified, the exact
   follow-on backlog row.
4. Write `ai_docs/items/scripting-service-runtime-state-extraction-design.md`
   deciding whether runtime-state extraction reduces risk while preserving
   watchdog invariants.
5. Write `ai_docs/items/initiative-executor-roslyn-tool-discovery-brief.md`
   documenting the Roslyn-first tool mapping, brief-injection plan, and whether
   a concrete edit or measurement follow-on is justified.
6. Add focused validation:
   - formatter whitespace verify on the selected slice;
   - AI-doc validation for new/updated planning and backlog files;
   - release validation for the code-formatting change.
7. backlog: sync `ai_docs/backlog.md` by removing completed selected rows and
   adding any justified follow-on rows.
8. changelog: add a fragment covering all completed selected rows.
