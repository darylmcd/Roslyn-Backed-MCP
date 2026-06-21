# Next work and backlog

<!-- purpose: Open work only. Slim-index format — triage in the table, implementation detail in items/<id>.md. Sync rows on ship. -->
<!-- scope: in-repo -->

**updated_at:** 2026-06-21T14:00:23Z

## Agent contract

| | |
|---|---|
| **Scope** | This file lists unfinished work only. It is not a changelog. |
| **MUST** | Remove or update backlog rows when work ships; do it in the same PR or an immediate follow-up. Closing a row also deletes its `ai_docs/items/<id>.md` — use `/close-backlog-rows`, which does both atomically. |
| **MUST** | End implementation plans with a final todo: `backlog: sync ai_docs/backlog.md`. |
| **MUST** | Use stable, kebab-case `id` values per open row. |
| **MUST** | Keep the `do` cell **slim** — a bold title + one concrete next deliverable + `[type: …]` + `[source: …]` tags (≤~250 chars). Enough to triage, not to implement. |
| **MUST** | Spill implementation detail (`Anchors:`, acceptance criteria, long-form evidence) to `ai_docs/items/<id>.md` for any **code-touching row**, and point the `detail` cell at it. Seed from `~/.claude/skills/doc-audit/templates/items.md`. Pure-prose rows (Defer rationale, decision notes) may stay inline with `detail: —`. |
| **MUST** | Set `size` per row: `S` (≤1 prod file) / `M` (2–4 prod files) / `L` (>4 prod files or >1 regression shape). `L` is a **split-candidate** — split it into per-slice children before planning against it. >3 test files is also a split-candidate. |
| **MUST** | Keep `deps` to backlog row ids or `—` (`none` ≡ `—`). A dep id matching a live open row = this row is blocked; an id absent from the backlog = satisfied (open-work-only). |
| **MUST NOT** | Add `Completed`, `Shipped`, `Done`, `History`, or `Changelog` sections. Git is the archive. |
| **MUST NOT** | Leave done items in the open table. |
| **MUST NOT** | Inline `Anchors:`/acceptance/multi-line evidence in a code-touching row's `do` cell, or add `### <id>` body sections per item. The slim row + its `items/<id>.md` are the canonical pair. |

## Standing rules

- **Reprioritize on each audit pass.** Stale priority order is a finding.
- **Keep rows planner-ready.** A row is ready when an agent can read it cold and start a plan: a clear title + next deliverable in the `do` cell, the live anchors in `items/<id>.md`.
- **Replace stale umbrella rows with concrete follow-ons** before planning against them.
- **Detail lives in `items/<id>.md`, evidence in referenced reports** — not in this file. The `do` cell carries the title + next step only; the detail file carries anchors + acceptance + a one-line evidence summary plus the report path.
- **Weak-evidence flag.** When a row's signal is thin (single retro session, self-audit only, etc.) say so explicitly in the `do` cell ("Weaker evidence — N until external session reproduces").
- **GitHub-issue cross-link.** When a row has a corresponding open GitHub Issue, surface the link at the start of the `do` cell so the backlog and issue stay paired. Two flavors:
  - **Reserved for contributor pickup** (`good first issue` / `help wanted` labels): prefix the `do` cell with `**Reserved — [gh #NNN](https://github.com/darylmcd/Roslyn-Backed-MCP/issues/NNN) (good first issue|help wanted); skip in sweeps until contributor pickup.**` — `/backlog-sweep:plan` skips these per its Step 1 hard-skip rule. Remove the marker (or close the row) when a contributor PR lands or when the maintainer reclaims the work.
  - **Tracked-only** (auto-filed audit issues that aren't promoted to a contributor label): prefix the `do` cell with `[gh #NNN](https://github.com/darylmcd/Roslyn-Backed-MCP/issues/NNN) — `. Sweep treats these as normal claimable rows; the implementing PR closes both the issue (via `Fixes #NNN`) and the backlog row.
- **Priority tiers:** Critical > High > Medium > Low > Defer.
- See `workflow.md` → **Backlog closure** for close-in-PR expectations.

---

## Critical

<!-- Production-breaking or blocking work. Empty section is fine; keep the header. -->

| id | pri | deps | do | size | detail |
|----|-----|------|----|------|--------|

## High

| id | pri | deps | do | size | detail |
|----|-----|------|----|------|--------|

## Medium

| id | pri | deps | do | size | detail |
|----|-----|------|----|------|--------|
| `promotion-tier-execution-batch` | Medium | — | **Promotion-tier execution batch** — re-run the promotion scorecard against the current v2.3.x surface (canonical snapshot is v1.38.1), then ship experimental→stable promotions in bounded batches via `/promote-tier`. Catalog hotspot; sweep-shaped → `/backlog-sweep:prepare`. [type: ops] | L | items/promotion-tier-execution-batch.md |
| `audit-21-analyzer-load-decision` | Medium | — | **AUDIT-21 analyzer-load decision** — execute the dormant IDE/CA analyzer-parity Draft plan via `/backlog-sweep:prepare`, OR re-status it superseded/parked with the product trigger; fix the plan's stale §13 row citation. Blocked-on product decision (full analyzer parity required?). | M | items/audit-21-analyzer-load-decision.md |
| `compilation-cache-adoption-read-side` | Medium | — | **Compilation-cache read-side adoption** — batches 1–2 shipped (#913/#936; ~10/24 sites); split the remaining site groups (incl. forked-solution hazard) into bounded child batches at `/backlog-sweep:prepare`. [type: refactor] | L | items/compilation-cache-adoption-read-side.md |

## Low

| id | pri | deps | do | size | detail |
|----|-----|------|----|------|--------|
| `aggregate-scorecard-includeself-double-count` | Low | — | **`-IncludeSelf` double-counts the hub repo** — `aggregate-promotion-scorecards.ps1` adds Roslyn-Backed-MCP via `-IncludeSelf` AND re-discovers it as a sibling (no self-exclusion), double-counting it in the scan counters; a double-counted hub vote could spuriously satisfy the 2-vote promote quorum. Latent (only with `-IncludeSelf`). [type: bug] [source: 2026-06-19 top-n implementer finding] | S | items/aggregate-scorecard-includeself-double-count.md |
| `externaledit-test-rearm-marker-file-type-neutral` | Low | — | **Watcher staleness test re-arm marker assumes C# comment syntax** — the dropped-event re-touch in `ExternalEditStalenessTests` appends `// watcher re-arm {guid}`, invalid markup if the helper is ever invoked with a `.csproj`/`.props`/`.targets`/`.sln` tracked file; append a file-type-neutral marker instead. [type: test] [source: 2026-06-19 top-n code-quality review] | S | items/externaledit-test-rearm-marker-file-type-neutral.md |
| `workspace-id-optional-readonly-surface-full-sweep` | Low | — | **workspaceId optional full sweep** — flip the remaining ~45 read-only tools REQUIRED→OPTIONAL; gate on the pilot's `_meta.autoResolution` adoption signal; sub-batch per `*Tools.cs` file. [type: feature] [source: 2026-06-09 backlog-sweep execute] | L | items/workspace-id-optional-readonly-surface-full-sweep.md |
| `tool-surface-pagination-or-tool-sets` | Low | — | **Tool-set catalog resources** — wait for post-`recommend_workflow` evidence, then add bounded tool-set catalog resources without hiding tools. Weaker evidence — N until small-model discovery friction is reported after the router lands externally. | M | items/tool-surface-pagination-or-tool-sets.md |
| `test-run-unfiltered-bare-error-rootcause` | Low | — | **test_run bare-error root cause** — determine TRX-overflow vs escaped-exception cause of the bare "An error occurred invoking test_run", then fix per cause. [source: 2026-05-31 surface-test + 2026-06-08 retro] | M | items/test-run-unfiltered-bare-error-rootcause.md |
| `apply-composite-preview-destructive-misnomer` | Low | — | **apply_composite_preview misnomer** — rename to `apply_composite` or loudly document that this `_preview`-suffixed tool applies (published surface — Directive #4 ADR + migration note). [source: 2026-05-31 surface-test] | M | items/apply-composite-preview-destructive-misnomer.md |
| hoststdio-middleware-tools-namespace-cycle | Low | — | **Middleware↔Tools namespace cycle** — break one direction (extract shared contract to a third namespace, or invert via interface) + architecture test. [type: refactor] [source: 2026-05-31 surface-test] | L | items/hoststdio-middleware-tools-namespace-cycle.md |
| `change-signature-callsite-summary-stale-row-comments` | Low | — | **Stale row id in prompt/test comments** — restore a bounded row for the callsite-summary limitation or rewrite the comments without the dead `change-signature-preview-callsite-summary` pointer. [type: docs] | S | items/change-signature-callsite-summary-stale-row-comments.md |
| `surface-test-shipped-prompt-local-skill-reference` | Low | — | **Shipped prompt cites maintainer-local .claude path** — replace with portable wording; consider genericity-guard rejection of `.claude/skills/` refs in shipped prompts. [type: docs] | M | items/surface-test-shipped-prompt-local-skill-reference.md |
| `scripting-sync-over-async-document` | Low | — | **Document the scripting sync-over-async fence** — add the approved-exception comment/marker at `ScriptingService.cs:152` + grep assertion. [type: refactor] | M | items/scripting-sync-over-async-document.md |
| `initiative-executor-roslyn-tool-discovery-experiment` | Low | — | **Executor Roslyn first-hop experiment** — measure post-`recommend_workflow` bypass (semantic-first-hop vs `Read`/`Grep`/`Edit` counts), produce a go/no-go note before editing the executor brief. [source: 2026-06-04 discovery-sweep + 2026-06-08 retro] | M | items/initiative-executor-roslyn-tool-discovery-experiment.md |
| `surface-snapshot-stale-surface-audit` | Low | — | **Refresh the live-surface snapshot** — run `/surface-audit` to update `.ai-doc-audit.md` counts (snapshot 2026-05-06 says 168 tools; surface ~173, server v2.3.2) before the next release cut. [type: docs] | M | items/surface-snapshot-stale-surface-audit.md |
| `shipped-skills-hardcode-bare-roslyn-tool-prefix` | Low | — | **Prefix-agnostic shipped skills** — VERIFY FIRST whether the plugin-prefix surface-test entry gate misfires, then sweep shipped `skills/**` + retro prompt to suffix-based tool references via the genericity guard. [type: bug] [source: 2026-06-08 retro follow-up] | L | items/shipped-skills-hardcode-bare-roslyn-tool-prefix.md |
| `backlog-d-fragment-schema` | Low | — | **Relocate the backlog.d fragment-schema doc out of items/** — it is a canonical cross-repo schema (cited by shipped `skills/mcp-server-surface-test` prompt + `.claude/skills/backlog-intake`), not row detail; move it and update referrers in one PR. [type: docs] [source: v15-migration-20260611] | S | items/backlog-d-fragment-schema.md |
| `workspace-auto-load-on-demand-design` | Low | — | **Retire the shipped auto-load design spec** — after `/reconcile-plans` GCs plan `20260609T134405Z`, fold residual unimplemented sections into the full-sweep row and delete the doc. [type: docs] [source: v15-migration-20260611] | S | items/workspace-auto-load-on-demand-design.md |
| `move-to-git-issues` | Low | — | **Disposition the parked move-to-git-issues design** — rows 1-3 shipped v1.35.1; decide row 4 + the doc's 4 open questions (file rows or record won't-do), then retire the doc. [type: docs] [source: v15-migration-20260611] | S | items/move-to-git-issues.md |
| `filewatcher-watcherentry-watchers-unguarded-mutation` | Low | — | **WatcherEntry._watchers mutated without synchronization** — `AddWatcher` does `_watchers.Add` on an unguarded `List<FileSystemWatcher>` while the rest of the type is `_reasonLock`-guarded. Benign today (single-threaded `Watch()`); document the invariant or guard. [type: bug] [source: 2026-06-20 top-n row-1 finding] | S | items/filewatcher-watcherentry-watchers-unguarded-mutation.md |
| `filewatcher-class-xmldoc-truncated` | Low | — | **FileWatcherService class XML doc clause is truncated** — the class-level `<para>` (`FileWatcherService.cs:~25`) ends "…server apply paths that want to preserve their attribution mark after the on-disk commit settles." with no main verb — a dropped clause. Complete or trim it. [type: docs] [source: 2026-06-20 top-n row-1 implementer finding] | S | items/filewatcher-class-xmldoc-truncated.md |
| `ci-policy-cache-version-stale-cite` | Low | — | **CI_POLICY.md cites a stale actions/cache version** — `CI_POLICY.md:12` says `actions/cache@v4` but `ci.yml:96` uses `actions/cache@v5`; sync the cite. [type: docs] [source: 2026-06-20 top-n row-2 implementer finding] | S | items/ci-policy-cache-version-stale-cite.md |
| `nuget-checker-timeout-test-bound-couple-to-httptimeout` | Low | — | **Couple the NuGet timeout-test wait bound to HttpTimeout** — the 30s hang-guard literal's `>> HttpTimeout(3s)` coupling is prose-only; derive it from a multiple of HttpTimeout, or close won't-fix. [type: test] [source: 2026-06-20 top-n row-4 cq] | S | items/nuget-checker-timeout-test-bound-couple-to-httptimeout.md |
| `reference-service-dead-iscorlib-single-arg-overload` | Low | — | **Remove dead IsCorlibAssembly overload** — `ReferenceService.cs:444` `IsCorlibAssembly(IAssemblySymbol?)` is unreferenced (sole site `:422` uses the two-arg form); delete it. [type: refactor] [source: 2026-06-20 compcache batch-a cq] | S | items/reference-service-dead-iscorlib-single-arg-overload.md |
| restore-build-required-classifier-consistency | Low | — | **Unify build-required diagnostic classification** — `HasBuildRequiredWorkspaceDiagnostics` (any analyzer warn) and `HasBuildRequiredDiagnostic` (substring-gated) diverge; the readiness verdict also bypasses the `BuildRequired` flag. Extract one classifier. [type: refactor] [source: 2026-06-21 sweep #1009 cq] | S | items/restore-build-required-classifier-consistency.md |
| workspace-unresolved-analyzer-message-wording | Low | — | **Reword WORKSPACE_UNRESOLVED_ANALYZER diagnostic** — message says cause may be "an unresolved package path" (restore) but the warning now routes to BuildRequired; lead with the `dotnet build` remedy. [type: docs] [source: 2026-06-21 sweep #1009 cq] | S | items/workspace-unresolved-analyzer-message-wording.md |
| nuget-vuln-scan-redundant-start-progress | Low | — | **Drop redundant nuget-scan start progress** — `SecurityTools.ScanNuGetVulnerabilities` emits a bare `Report(0,1)` right before `ReportStage(0,1,"scanning-nuget")`; open directly with ReportStage like ValidationTools/WorkspaceWarmTools. [type: refactor] [source: 2026-06-21 sweep #1008 cq] | S | items/nuget-vuln-scan-redundant-start-progress.md |
| dup-method-detector-test-setup-dedup | Low | — | **De-dup DuplicateMethodDetectorTests workspace setup** — `BuildServiceAndGate` copy-pastes ~22 lines from `BuildServiceWithSourcesCore` (delta: the added gate); reuse the existing builder. [type: test] [source: 2026-06-21 sweep #1011 cq] | S | items/dup-method-detector-test-setup-dedup.md |
| exception-flow-throwsite-test-and-arm-dedup | Low | — | **trace_exception_flow throw-site exclusion test + arm dedup** — add a test that an unrelated thrown type is excluded from ThrowSites; dedupe the bounded-add across the throw-node switch arms. [type: test] [source: 2026-06-21 sweep #1015 cq] | S | items/exception-flow-throwsite-test-and-arm-dedup.md |

## Defer

<!-- Explicitly parked. Record WHY in the `do` cell. -->

| id | pri | deps | do | size | detail |
|----|-----|------|----|------|--------|
| `http-streamable-host-project` | Defer | — | **HTTP Streamable host project** — parked until a concrete remote-deployment driver (named users, auth/observability/tenancy plan approved and staffed); multi-week design. | — | items/http-streamable-host-project.md |
| `roslyn-mcp-cross-repo-steering-gap` | Defer | — | **Cross-repo steering gap** — parked on product decision: push adoption (consumer AGENTS.md steering / lower first-hop cost) or accept current usage. Premise corrected 2026-06-08 — usage is real, gap is frequency/reach. Weaker evidence — frequency inference. [type: docs] | — | items/roslyn-mcp-cross-repo-steering-gap.md |
| `workspace-process-pool-or-daemon` | Defer | — | **Workspace daemon / process pool** — parked until a worse-than-OrchardCore profile or daily-use evidence shows `workspace_load`/reload P95 blocking work after `workspace_warm`. | — | items/workspace-process-pool-or-daemon.md |

## Refs

| Path | Role |
|------|------|
| `ai_docs/planning_index.md` | Planning router and scope boundary |
| `ai_docs/workflow.md` | Branch/PR workflow and backlog-closure rule |
| `ai_docs/items/` | Per-row implementation detail (Anchors/Acceptance/Evidence); seed new files from `~/.claude/skills/doc-audit/templates/items.md` |
| `ai_docs/bootstrap-read-tool-primer.md` | Self-edit session read-only tool primer (Roslyn-MCP read-side tools to prefer over Bash/Grep) |
| `ai_docs/runtime.md` | Bootstrap scope policy — distinguishes main-checkout self-edit (no `*_apply`) from worktree/parallel-subagent sessions |
| `docs/large-solution-profiling-baseline.md` | Evidence gate for daemon/process-pool performance work |
| `ai_docs/procedures/deep-review-backlog-intake.md` | Intake procedure for future audit batches |
| `review-inbox/` | Staging folder for the NEXT audit batch (flat directory; `/backlog-intake` reads here) |
| `review-inbox/archive/<batch-ts>/` | Processed audit/retro/promotion batches; delete after all actionable items are shipped, rejected, or summarized |
