# Next work and backlog

<!-- purpose: Open work only. Slim-index format — triage in the table, implementation detail in items/<id>.md. Sync rows on ship. -->
<!-- scope: in-repo -->

**updated_at:** 2026-06-19T18:17:27Z

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
| `ci-vuln-audit-gating` | Medium | — | **Make the CI vulnerability audit blocking** — `dotnet package list --vulnerable` in `ci.yml` exits 0 even when CVEs are present, so a vulnerable transitive dep does not fail the build; parse its output (or add a gate) to fail on findings. [type: ci] [source: 2026-06-19 CI review] | S | items/ci-vuln-audit-gating.md |
| `repo-dependabot-config` | Medium | — | **Add Dependabot (actions + nuget)** — no `.github/dependabot.yml`/Renovate exists; add `github-actions` + `nuget` ecosystems and bump the stale `actions/*@v4` (checkout/cache/upload-artifact) to clear the Node 20 deprecation warning. [type: ci] [source: 2026-06-19 CI review] | S | items/repo-dependabot-config.md |
| `nuget-mcpserver-gallery-packaging` | Medium | — | **Publish to the NuGet MCP gallery** — add `<PackageType>McpServer</PackageType>` (coexists with `PackAsTool`) + embed `.mcp/server.json` so `Darylmcd.RoslynMcp` lists under nuget.org `?packagetype=mcpserver` with an MCP-Server tab. Deferred from #976 — needs a pack-tested PR. [type: packaging] [source: 2026-06-19 registry-publish] | M | items/nuget-mcpserver-gallery-packaging.md |
| `filewatcher-waitforstale-clearstale-stranded-awaiter` | Medium | — | **WaitForStaleAsync awaiter stranded by concurrent ClearStale** — `FileWatcherService.ClearStale` swaps `_staleSignal` for a fresh TCS without completing the outgoing one, so an awaiter parked on the prior signal hangs until its `CancellationToken` deadline. Benign for the sole current caller (the staleness test, bounded by a 5s CTS) but a latent trap for the next production caller. [type: bug] [source: 2026-06-19 top-n code-quality review] | S | items/filewatcher-waitforstale-clearstale-stranded-awaiter.md |

## Low

| id | pri | deps | do | size | detail |
|----|-----|------|----|------|--------|
| `registry-readiness-linter-warn-relax` | Low | — | **Relax the registry-readiness `repository-url-matches-name` warn** — it derives the expected repo URL from the server NAME and warns when they differ, but GitHub auth grants the whole `io.github.<owner>/*` namespace, so the name segment intentionally differs from the repo (`roslyn-mcp` vs `Roslyn-Backed-MCP`). Accept any name under the owner namespace. [type: tooling] [source: 2026-06-19 registry-publish] | S | items/registry-readiness-linter-warn-relax.md |
| `externaledit-test-rearm-marker-file-type-neutral` | Low | — | **Watcher staleness test re-arm marker assumes C# comment syntax** — the dropped-event re-touch in `ExternalEditStalenessTests` appends `// watcher re-arm {guid}`, invalid markup if the helper is ever invoked with a `.csproj`/`.props`/`.targets`/`.sln` tracked file; append a file-type-neutral marker instead. [type: test] [source: 2026-06-19 top-n code-quality review] | S | items/externaledit-test-rearm-marker-file-type-neutral.md |
| `workspace-id-optional-readonly-surface-full-sweep` | Low | — | **workspaceId optional full sweep** — flip the remaining ~45 read-only tools REQUIRED→OPTIONAL; gate on the pilot's `_meta.autoResolution` adoption signal; sub-batch per `*Tools.cs` file. [type: feature] [source: 2026-06-09 backlog-sweep execute] | L | items/workspace-id-optional-readonly-surface-full-sweep.md |
| `aggregate-scorecard-stale-search-path` | Low | — | **Aggregator stale scorecard probe** — drop/demote the removed `ai_docs/audit-reports/` first-probe in `$ScorecardSearchPaths`; reconcile the contradicting SKILL docs. [type: refactor] | M | items/aggregate-scorecard-stale-search-path.md |
| `tool-surface-pagination-or-tool-sets` | Low | — | **Tool-set catalog resources** — wait for post-`recommend_workflow` evidence, then add bounded tool-set catalog resources without hiding tools. Weaker evidence — N until small-model discovery friction is reported after the router lands externally. | M | items/tool-surface-pagination-or-tool-sets.md |
| `parameter-naming-canonicalization-migration` | Low | `parameter-naming-canonicalization-experimental-surface` | **Parameter-naming canonicalization migration** — rename the 5 `projectFilter`/`scopeProjectFilter` params to `projectName` per the approved design + lockstep tests + `Changed — BREAKING` changelog entry. [type: refactor] | M | items/parameter-naming-canonicalization-migration.md |
| `test-run-unfiltered-bare-error-rootcause` | Low | — | **test_run bare-error root cause** — determine TRX-overflow vs escaped-exception cause of the bare "An error occurred invoking test_run", then fix per cause. [source: 2026-05-31 surface-test + 2026-06-08 retro] | M | items/test-run-unfiltered-bare-error-rootcause.md |
| `apply-composite-preview-destructive-misnomer` | Low | — | **apply_composite_preview misnomer** — rename to `apply_composite` or loudly document that this `_preview`-suffixed tool applies (published surface — Directive #4 ADR + migration note). [source: 2026-05-31 surface-test] | M | items/apply-composite-preview-destructive-misnomer.md |
| `find-duplicated-methods-no-byte-budget` | Low | — | **find_duplicated_methods byte budget** — add an output-byte budget / summary mode to shared `FindDuplicatedMethodsCore` so large result sets degrade gracefully (premise corrected 2026-06-05; canonical + alias affected equally). [type: perf] | M | items/find-duplicated-methods-no-byte-budget.md |
| `hoststdio-middleware-tools-namespace-cycle` | Low | — | **Middleware↔Tools namespace cycle** — break one direction (extract shared contract to a third namespace, or invert via interface) + architecture test. [type: refactor] [source: 2026-05-31 surface-test] | M | items/hoststdio-middleware-tools-namespace-cycle.md |
| `nuget-vuln-scan-exceeds-budget` | Low | — | **nuget_vulnerability_scan budget** — cache the per-restore result on the lock-file hash and/or add a heartbeat + documented longer budget for the network-bound scan. [type: perf] [source: 2026-05-31 surface-test] | M | items/nuget-vuln-scan-exceeds-budget.md |
| `restore-required-vs-build-conflation` | Low | — | **restoreRequired vs buildRequired** — distinguish a `buildRequired`/build-hint state when the unmet dependency is an analyzer/project build output, not a NuGet restore input. [source: 2026-05-31 surface-test] | M | items/restore-required-vs-build-conflation.md |
| `revert-last-apply-single-slot-doc-warning` | Low | — | **revert_last_apply single-slot warning** — state the single-slot LIFO behaviour loudly in the description; cross-point to `revert_apply_by_sequence`. [type: docs] [source: 2026-05-31 surface-test] | S | items/revert-last-apply-single-slot-doc-warning.md |
| `change-signature-callsite-summary-stale-row-comments` | Low | — | **Stale row id in prompt/test comments** — restore a bounded row for the callsite-summary limitation or rewrite the comments without the dead `change-signature-preview-callsite-summary` pointer. [type: docs] | S | items/change-signature-callsite-summary-stale-row-comments.md |
| `surface-test-shipped-prompt-local-skill-reference` | Low | — | **Shipped prompt cites maintainer-local .claude path** — replace with portable wording; consider genericity-guard rejection of `.claude/skills/` refs in shipped prompts. [type: docs] | M | items/surface-test-shipped-prompt-local-skill-reference.md |
| `test-discover-no-autopagination` | Low | — | **test_discover pagination** — add offset/limit (or summary mode) so the unfiltered call degrades gracefully instead of breaching the MCP token cap. [source: 2026-05-31 surface-test] | M | items/test-discover-no-autopagination.md |
| `trace-exception-flow-no-throwsite` | Low | — | **trace_exception_flow throw sites** — add the throw-site half, raise/declare the cap, rank type-specific catches above base-Exception catches. [source: 2026-05-31 surface-test] | M | items/trace-exception-flow-no-throwsite.md |
| `worktree-teardown-windows-lock-multi-drain` | Low | — | **workspace_close drains testhost** — terminate detached `testhost.exe`/`vstest.console` (or bounded poll-until-released) so worktree removal succeeds after `test_run`. [source: 2026-05-31 surface-test] | M | items/worktree-teardown-windows-lock-multi-drain.md |
| `scripting-sync-over-async-document` | Low | — | **Document the scripting sync-over-async fence** — add the approved-exception comment/marker at `ScriptingService.cs:152` + grep assertion. [type: refactor] | M | items/scripting-sync-over-async-document.md |
| `initiative-executor-roslyn-tool-discovery-experiment` | Low | — | **Executor Roslyn first-hop experiment** — measure post-`recommend_workflow` bypass (semantic-first-hop vs `Read`/`Grep`/`Edit` counts), produce a go/no-go note before editing the executor brief. [source: 2026-06-04 discovery-sweep + 2026-06-08 retro] | M | items/initiative-executor-roslyn-tool-discovery-experiment.md |
| `surface-snapshot-stale-surface-audit` | Low | — | **Refresh the live-surface snapshot** — run `/surface-audit` to update `.ai-doc-audit.md` counts (snapshot 2026-05-06 says 168 tools; surface ~173, server v2.3.2) before the next release cut. [type: docs] | M | items/surface-snapshot-stale-surface-audit.md |
| `shipped-skills-hardcode-bare-roslyn-tool-prefix` | Low | — | **Prefix-agnostic shipped skills** — VERIFY FIRST whether the plugin-prefix surface-test entry gate misfires, then sweep shipped `skills/**` + retro prompt to suffix-based tool references via the genericity guard. [type: bug] [source: 2026-06-08 retro follow-up] | L | items/shipped-skills-hardcode-bare-roslyn-tool-prefix.md |
| `backlog-d-fragment-schema` | Low | — | **Relocate the backlog.d fragment-schema doc out of items/** — it is a canonical cross-repo schema (cited by shipped `skills/mcp-server-surface-test` prompt + `.claude/skills/backlog-intake`), not row detail; move it and update referrers in one PR. [type: docs] [source: v15-migration-20260611] | S | items/backlog-d-fragment-schema.md |
| `workspace-auto-load-on-demand-design` | Low | — | **Retire the shipped auto-load design spec** — after `/reconcile-plans` GCs plan `20260609T134405Z`, fold residual unimplemented sections into the full-sweep row and delete the doc. [type: docs] [source: v15-migration-20260611] | S | items/workspace-auto-load-on-demand-design.md |
| `move-to-git-issues` | Low | — | **Disposition the parked move-to-git-issues design** — rows 1-3 shipped v1.35.1; decide row 4 + the doc's 4 open questions (file rows or record won't-do), then retire the doc. [type: docs] [source: v15-migration-20260611] | S | items/move-to-git-issues.md |
| `legacy-bug-id-msbuild-eval-comments` | Low | — | **Strip remaining BUG-008 ids from MSBuild eval service** — remove the internal `BUG-008` id from `MsBuildEvaluationService.cs:84` + `IMsBuildEvaluationService.cs:23`, keeping the filter-rationale text. [type: docs] [source: 2026-06-18 backlog-sweep execute] | M | items/legacy-bug-id-msbuild-eval-comments.md |
| `symboltools-twin-null-guard-comment-exemplars` | Low | — | **Align SymbolTools twin null-guard comment citations** — the two NotFound-envelope guard comments cite different siblings (`member_hierarchy` vs `symbol_info`); unify them. [type: docs] [source: 2026-06-18 backlog-sweep execute] | S | items/symboltools-twin-null-guard-comment-exemplars.md |
| `workspace-validation-kill-test-reflection-seam` | Low | — | **Drop reflection in kill-failure test** — drive the `WorkspaceValidationService` kill-failure log path via the injected `killProcessTree` seam, not reflection into private `TryKillProcessTree`. [type: test] [source: 2026-06-18 backlog-sweep execute] | S | items/workspace-validation-kill-test-reflection-seam.md |

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
