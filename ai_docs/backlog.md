# Next work and backlog

<!-- purpose: Open work only. Slim-index format — triage in the table, implementation detail in items/<id>.md. Sync rows on ship. -->
<!-- scope: in-repo -->

**updated_at:** 2026-08-05T21:15:00Z

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
| `extract-type-preview-refusal-missing-blocking-deps` | High | — | **extract_type_preview refusals give no retry path** — surface the already-collected dangling-reference warnings as structured `blockingDependencies` data (not just prose) so callers can retry with a corrected memberNames set instead of abandoning the tool. [type: feature] [source: 2026-08-05 multisession retro] | M | items/extract-type-preview-refusal-missing-blocking-deps.md |
| `validate-workspace-compiler-category-status-mismatch` | High | — | **validate_workspace reports compile-error on a clean build** — `ComputeOverallStatus` flags `compile-error` whenever the separately-harvested `diagResult.CompilerDiagnostics` contains a Category="Compiler" error, even when `compile.ErrorCount==0`; reconcile the two error sources. [type: bug] [source: 2026-08-05 multisession retro] | S | items/validate-workspace-compiler-category-status-mismatch.md |
| `workspace-eviction-no-auto-retry-on-tool-call` | High | — | **Auto-retry once on WorkspaceEvictedException** — `compile_check`/`test_run` surface a hard failure instead of transparently reloading an evicted workspace and retrying once; TTL-refresh-on-touch already exists (`TouchAccess`), only the retry wrapper is missing. Caused a non-deterministic test failure in-window. [type: reliability] [source: 2026-08-05 multisession retro] | M | items/workspace-eviction-no-auto-retry-on-tool-call.md |

## Medium

| id | pri | deps | do | size | detail |
|----|-----|------|----|------|--------|
| `promotion-tier-execution-batch` | Medium | — | **Promotion-tier execution batch** — re-run the promotion scorecard against the current v2.3.x surface (canonical snapshot is v1.38.1), then ship experimental→stable promotions in bounded batches via `/promote-tier`. Catalog hotspot; sweep-shaped → `/backlog-sweep:prepare`. [type: ops] | L | items/promotion-tier-execution-batch.md |
| `audit-21-analyzer-load-decision` | Medium | — | **AUDIT-21 analyzer-load decision** — execute the dormant IDE/CA analyzer-parity Draft plan via `/backlog-sweep:prepare`, OR re-status it superseded/parked with the product trigger; fix the plan's stale §13 row citation. Blocked-on product decision (full analyzer parity required?). | M | items/audit-21-analyzer-load-decision.md |
| `ci-runner-offline-hosted-fallback-router` | Medium | — | **Hosted-fallback router for the self-hosted runner** — add an ubuntu router job that probes runner online-status and feeds `runs-on` via needs-outputs so PRs stop queueing 2–14.5h when the box is asleep/wedged. Operator gate: needs a repo-admin-scope PAT secret (workflow GITHUB_TOKEN cannot read the runners API). [type: infra] [source: 2026-07-14 CI-hang investigation] | S | items/ci-runner-offline-hosted-fallback-router.md |
| `compilation-cache-adoption-read-side` | Medium | — | **Compilation-cache read-side adoption** — batches 1–2 shipped (#913/#936; ~10/24 sites); split the remaining site groups (incl. forked-solution hazard) into bounded child batches at `/backlog-sweep:prepare`. [type: refactor] | L | items/compilation-cache-adoption-read-side.md |
| `compile-check-multi-project-fallback-structured-scope` | Medium | — | **compile_check's multi-project fallback is prose-only** — expose `actualScope`/`requestedScope` as structured DTO fields so callers can detect the full-project-compile fallback without parsing `restoreHint` text. Deterministic across 5 sessions/4 repos in-window; results stay correct, cost is latency. [type: feature] [source: 2026-08-05 multisession retro] | M | items/compile-check-multi-project-fallback-structured-scope.md |
| `core-dto-location-quartet-consolidation-secondary` | Medium | core-dto-location-quartet-consolidation-primary | **Compose LocationDto in PropertyWriteDto/TypeMutationDto** — apply the same LocationDto composition to PropertyWriteDto and TypeMutationDto's MutationCallerDto once the primary pattern lands. [type: refactor] [source: refactor-matrix-pass1] | M | items/core-dto-location-quartet-consolidation-secondary.md |
| `direct-mutation-undo-byte-fidelity` | Medium | — | Preserve byte-exact undo snapshots across direct edit, editorconfig, and project mutation paths. [type: bug] [source: 2026-08-05 direct remediation adjacent review] | M | items/direct-mutation-undo-byte-fidelity.md |
| `recommend-workflow-missing-semantic-grep-route` | Medium | — | **recommend_workflow has no semantic_grep routing branch** — its 5 `ContainsAny` rules cover references/outline/compile/tests/rename but nothing routes pattern-search-shaped tasks ("find usages of pattern X") to `semantic_grep`, so agents default to grep. Only pattern confirmed identical across both harnesses in-window. [type: feature] [source: 2026-08-05 multisession retro] | S | items/recommend-workflow-missing-semantic-grep-route.md |
| `validate-recent-git-changes-status-timeout-false-clean` | Medium | — | **git-status timeout silently reports clean** — the 10s `_gitStatusTimeout` fallback in `GetGitChangedFilesAsync` returns an empty changed-file list on timeout instead of a degraded/unknown signal, distinct from the already-fixed broader validation-phase timeout (closed gh #759). [type: bug] [source: 2026-08-05 multisession retro] | S | items/validate-recent-git-changes-status-timeout-false-clean.md |

## Low

| id | pri | deps | do | size | detail |
|----|-----|------|----|------|--------|
| `workspace-id-optional-readonly-surface-full-sweep` | Low | — | **workspaceId optional full sweep** — flip the remaining ~45 read-only tools REQUIRED→OPTIONAL; gate on the pilot's `_meta.autoResolution` adoption signal; sub-batch per `*Tools.cs` file. [type: feature] [source: 2026-06-09 backlog-sweep execute] | L | items/workspace-id-optional-readonly-surface-full-sweep.md |
| `tool-surface-pagination-or-tool-sets` | Low | — | **Tool-set catalog resources** — wait for post-`recommend_workflow` evidence, then add bounded tool-set catalog resources without hiding tools. Weaker evidence — N until small-model discovery friction is reported after the router lands externally. | M | items/tool-surface-pagination-or-tool-sets.md |
| `test-run-unfiltered-bare-error-rootcause` | Low | — | **test_run bare-error root cause** — determine TRX-overflow vs escaped-exception cause of the bare "An error occurred invoking test_run", then fix per cause. [source: 2026-05-31 surface-test + 2026-06-08 retro] | M | items/test-run-unfiltered-bare-error-rootcause.md |
| `apply-composite-preview-destructive-misnomer` | Low | — | **apply_composite_preview misnomer** — rename to `apply_composite` or loudly document that this `_preview`-suffixed tool applies (published surface — Directive #4 ADR + migration note). [source: 2026-05-31 surface-test] | M | items/apply-composite-preview-destructive-misnomer.md |
| `hoststdio-middleware-tools-namespace-cycle` | Low | — | **Middleware↔Tools namespace cycle** — break one direction (extract shared contract to a third namespace, or invert via interface) + architecture test. [type: refactor] [source: 2026-05-31 surface-test] | L | items/hoststdio-middleware-tools-namespace-cycle.md |
| `change-signature-callsite-summary-stale-row-comments` | Low | — | **Stale row id in prompt/test comments** — restore a bounded row for the callsite-summary limitation or rewrite the comments without the dead `change-signature-preview-callsite-summary` pointer. [type: docs] | S | items/change-signature-callsite-summary-stale-row-comments.md |
| `surface-test-shipped-prompt-local-skill-reference` | Low | — | **Shipped prompt cites maintainer-local .claude path** — replace with portable wording; consider genericity-guard rejection of `.claude/skills/` refs in shipped prompts. [type: docs] | M | items/surface-test-shipped-prompt-local-skill-reference.md |
| `initiative-executor-roslyn-tool-discovery-experiment` | Low | — | **Executor Roslyn first-hop experiment** — measure post-`recommend_workflow` bypass (semantic-first-hop vs `Read`/`Grep`/`Edit` counts), produce a go/no-go note before editing the executor brief. [source: 2026-06-04 discovery-sweep + 2026-06-08 retro] | M | items/initiative-executor-roslyn-tool-discovery-experiment.md |
| `surface-snapshot-stale-surface-audit` | Low | — | **Refresh the live-surface snapshot** — run `/surface-audit` to update `.ai-doc-audit.md` counts (snapshot 2026-05-06 says 168 tools; surface ~173, server v2.3.2) before the next release cut. [type: docs] | M | items/surface-snapshot-stale-surface-audit.md |
| `shipped-skills-hardcode-bare-roslyn-tool-prefix` | Low | — | **Prefix-agnostic shipped skills** — VERIFY FIRST whether the plugin-prefix surface-test entry gate misfires, then sweep shipped `skills/**` + retro prompt to suffix-based tool references via the genericity guard. [type: bug] [source: 2026-06-08 retro follow-up] | L | items/shipped-skills-hardcode-bare-roslyn-tool-prefix.md |
| `backlog-d-fragment-schema` | Low | — | **Relocate the backlog.d fragment-schema doc out of items/** — it is a canonical cross-repo schema (cited by shipped `skills/mcp-server-surface-test` prompt + `.claude/skills/backlog-intake`), not row detail; move it and update referrers in one PR. [type: docs] [source: v15-migration-20260611] | S | items/backlog-d-fragment-schema.md |
| `workspace-auto-load-on-demand-design` | Low | — | **Retire the shipped auto-load design spec** — after `/reconcile-plans` GCs plan `20260609T134405Z`, fold residual unimplemented sections into the full-sweep row and delete the doc. [type: docs] [source: v15-migration-20260611] | S | items/workspace-auto-load-on-demand-design.md |
| `move-to-git-issues` | Low | — | **Disposition the parked move-to-git-issues design** — rows 1-3 shipped v1.35.1; decide row 4 + the doc's 4 open questions (file rows or record won't-do), then retire the doc. [type: docs] [source: v15-migration-20260611] | S | items/move-to-git-issues.md |
| `ci-policy-cache-version-stale-cite` | Low | — | **CI_POLICY.md cites a stale actions/cache version** — `CI_POLICY.md:12` says `actions/cache@v4` but `ci.yml:96` uses `actions/cache@v5`; sync the cite. [type: docs] [source: 2026-06-20 top-n row-2 implementer finding] | S | items/ci-policy-cache-version-stale-cite.md |
| `nuget-checker-timeout-test-bound-couple-to-httptimeout` | Low | — | **Couple the NuGet timeout-test wait bound to HttpTimeout** — the 30s hang-guard literal's `>> HttpTimeout(3s)` coupling is prose-only; derive it from a multiple of HttpTimeout, or close won't-fix. [type: test] [source: 2026-06-20 top-n row-4 cq] | S | items/nuget-checker-timeout-test-bound-couple-to-httptimeout.md |
| `filewatcher-markstaleifrelevant-stale-precedence-comment` | Low | — | **MarkStaleIfRelevant comment claims external-edit precedence the code lacks** — `FileWatcherService.cs:154-155` says external edits take precedence / no-downgrade, but `MarkStaleWithReason` (:247) is unconditional last-writer-wins; fix or delete the stale comment. [type: docs] [source: 2026-06-21 top-n cold-review] | S | items/filewatcher-markstaleifrelevant-stale-precedence-comment.md |
| `analysis-services-dedup-reference-classifiers` | Low | — | **Unify the three independent reference-site classifiers in Roslyn analysis services** — merge ConsumerAnalysisService.ClassifyDependencyKind, TypeConsumersService.ClassifyKind. [type: refactor] [source: refactor-matrix-pass1] | M | items/analysis-services-dedup-reference-classifiers.md |
| `analysis-services-hardcoded-parallelism-clamp-magic-numbers` | Low | — | **Centralize the duplicated parallelism-clamp and regex-timeout magic numbers in Roslyn analysis services** — replace the three independent Math.Clamp(Environment.ProcessorCount, 4. [type: refactor] [source: refactor-matrix-pass1] | M | items/analysis-services-hardcoded-parallelism-clamp-magic-numbers.md |
| `core-dto-fileeditsdto-array-to-readonlylist` | Low | — | **Fix FileEditsDto mutable array property** — change FileEditsDto.Edits from TextEditDto[] to IReadOnlyList<TextEditDto> to restore record value-equality and prevent aliased-array mutation of shared. [type: refactor] [source: refactor-matrix-pass1] | S | items/core-dto-fileeditsdto-array-to-readonlylist.md |
| `host-tools-cohesion-split` | Low | — | **Split low-cohesion Host tool files by responsibility** — break AdvancedAnalysisTools.cs's 11 unrelated endpoints (dead-code, DI registrations, complexity, reflection, namespace deps, NuGet deps. [type: refactor] [source: refactor-matrix-pass1] | M | items/host-tools-cohesion-split.md |
| `symbollocatorfactory-drift-tool-test-gap` | Low | — | **Add unit coverage for SymbolLocatorFactory and workspace_drift_check** — write direct unit tests for SymbolLocatorFactory.Create(. [type: refactor] [source: refactor-matrix-pass1] | M | items/symbollocatorfactory-drift-tool-test-gap.md |
| `stdoutwrite-analyzer-project-misplacement` | Low | — | **Move StdoutWriteAnalyzer out of the ServerSurfaceCatalog project** — relocate RMCP010's StdoutWriteAnalyzer (and update the RootNamespace/AssemblyName. [type: refactor] [source: refactor-matrix-pass1] | M | items/stdoutwrite-analyzer-project-misplacement.md |
| `static-singleton-di-bypass-core-services` | Low | — | **Replace static singleton DI-bypass state with scoped services** — convert WorkspaceEvictionRegistry and AmbientGateMetrics from static/AsyncLocal globals into DI-registered scoped services (or. [type: refactor] [source: refactor-matrix-pass1] | M | items/static-singleton-di-bypass-core-services.md |
| `consolidate-consumer-analysis-services` | Low | — | **Consolidate overlapping consumer-analysis service interfaces** — unify IConsumerAnalysisService.FindConsumersAsync and ITypeConsumersService.FindTypeConsumersAsync behind one contract (or document. [type: refactor] [source: refactor-matrix-pass1] | M | items/consolidate-consumer-analysis-services.md |
| `iedit-service-param-object` | Low | — | **Introduce shared options object for IEditService's repeated bool parameters** — replace the duplicated skipSyntaxCheck/verify/autoRevertOnError trio across ApplyTextEditsAsync and. [type: refactor] [source: refactor-matrix-pass1] | S | items/iedit-service-param-object.md |
| `capturelogger-shared-consumer-doc-drift` | Low | — | **Keep CaptureLogger documentation aligned with shared consumers** — replace the stale fixed consumer list with durable shared-helper wording. [type: test] [source: PR #1075 review] | S | items/capturelogger-shared-consumer-doc-drift.md |
| `dedupe-namespace-folder-segment-resolution` | Low | scaffolding-hotspot-complexity-reduction | **Share namespace folder-segment resolution** — consolidate duplicate namespace-to-relative-folder rules in scaffolding and parameter-object services. [type: refactor] [source: backlog-sweep-20260716-review] | M | items/dedupe-namespace-folder-segment-resolution.md |
| `apply-undo-tool-response-contract-docs` | Low | apply-undo-workflow-service-extraction | **Document apply and undo response variants** — enumerate discriminators, exact properties, explicit nulls, and recovery actions for all eight wire shapes. [type: docs] [source: backlog-sweep-20260716-review] | M | items/apply-undo-tool-response-contract-docs.md |
| `prompt-smoke-tests-concern-split` | Low | prompt-workflows-missing-test-coverage,prompt-shim-parameter-binding-complexity-extraction | **Split prompt smoke-test concerns** — separate builder smoke, prompt-shim binding/error coverage, and shared sample/workspace fixtures. [type: test-refactor] [source: backlog-sweep-20260716-review] | S | items/prompt-smoke-tests-concern-split.md |
| `legacy-sln-slnx-parity-drift` | Low | — | **Retire the legacy `Roslyn-Backed-MCP.sln`** — delete it (preferred; only live ref is `docs/setup.md:23`) or gate it against `RoslynMcp.slnx`. Already drifted: the `.sln` lists `samples/**` projects the `.slnx` lacks, nothing enforces parity. [type: maintenance] [source: 2026-07-19 slnx session] | S | items/legacy-sln-slnx-parity-drift.md |
| `edit-preview-validation-decomposition` | Low | direct-mutation-undo-byte-fidelity | Decompose edit validation and multi-file preview construction without changing coordinate or syntax behavior. [type: quality] [source: 2026-08-05 direct remediation adjacent review] | M | items/edit-preview-validation-decomposition.md |
| `refactoring-format-range-preview-decomposition` | Low | — | Decompose format-range preview and splice orchestration while preserving range and trivia behavior. [type: quality] [source: 2026-08-05 direct remediation adjacent review] | M | items/refactoring-format-range-preview-decomposition.md |
| `refactoring-code-fix-preview-decomposition` | Low | refactoring-format-range-preview-decomposition | Decompose diagnostic code-fix preview selection and assembly while preserving fix behavior. [type: quality] [source: 2026-08-05 direct remediation adjacent review] | M | items/refactoring-code-fix-preview-decomposition.md |

## Defer

<!-- Explicitly parked. Record WHY in the `do` cell. -->

| id | pri | deps | do | size | detail |
|----|-----|------|----|------|--------|
| `http-streamable-host-project` | Defer | — | **HTTP Streamable host project** — parked until a concrete remote-deployment driver (named users, auth/observability/tenancy plan approved and staffed); multi-week design. | — | items/http-streamable-host-project.md |
| `roslyn-mcp-cross-repo-steering-gap` | Defer | — | **Cross-repo steering gap** — parked on product decision: push adoption (consumer AGENTS.md steering / lower first-hop cost) or accept current usage. Premise corrected 2026-06-08 — usage is real, gap is frequency/reach. Weaker evidence — frequency inference. [type: docs] | — | items/roslyn-mcp-cross-repo-steering-gap.md |
| `workspace-process-pool-or-daemon` | Defer | — | **Workspace daemon / process pool** — parked until a worse-than-OrchardCore profile or daily-use evidence shows `workspace_load`/reload P95 blocking work after `workspace_warm`. | — | items/workspace-process-pool-or-daemon.md |
| `core-dto-location-quartet-consolidation-primary` | Defer | — | **Decide public LocationDto migration contract** — park until an ADR chooses serialized additive deprecation or major removal; an ignored in-process view is not migration. [type: decision] [source: backlog-sweep-20260716-review] | L | items/core-dto-location-quartet-consolidation-primary.md |

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
