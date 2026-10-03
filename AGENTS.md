# Agent Guidelines

Stable AI bootstrap entry point for the Roslyn-Backed-MCP repository (C# MCP server, published as `roslyn-mcp@roslyn-mcp-marketplace`).

## File Purpose (Critical)

This file is a bootstrap router, not a complete instruction set. Always execute **Session Start (Required)** before performing any task. Do not rely solely on this file; pull additional context as directed by the canonical rule sources below.

## Standing Engineering Directives

Restated from `~/.claude/CLAUDE.md` (canonical source). These eight directive **cores** (the bold titles) override expedience and are verbatim — do not summarize, drop, or alter them. The one-line gloss after each is a condensed summary for quick reference; the authoritative `Fires`/`Prevents`/`Edge` detail lives in `~/.claude/CLAUDE.md`.

1. **Correct fix > quick or cheap fix.** Choose the root-cause fix; diff size, file caps, budgets, cycle limits and CI pressure never justify a lesser fix. Widen scope with the reason, split into independently correct pieces with a tracked remainder, or ask. Never weaken a test, suppress, shim, duplicate or defer your own defect. Genuinely blocked work means an operator/product decision, externally owned code or an environment you cannot provision: provide current-session evidence, ask where needed and track the root-cause fix before shipping anything lesser.
2. **Optimize for AI consumption by default.** Write AI-facing files (`AGENTS.md`, `ai_docs/**`, prompts, planning/runtime/audit docs) as machine input: tables over prose, structured data over paragraphs, pointers over duplication. Human-facing files (`README.md` landing pages, `docs/**`) get prose.
3. **Bad code is never silent.** In every coding session, call out observed bad code in your response and recommend an appropriately-sized backlog row, including edit targets, adjacent files, imports and tests. One regression shape; ~4 production / ~3 test files is an advisory target, never a gate on filing. Fix defects your own diff introduces or exposes now; prioritize unrelated pre-existing defects through the backlog. Editing a bad section does not absolve flagging it.
4. **Private repos accept breaking changes.** For private repos, breaking changes and large refactors are the standing default when pursuing #1 or #3; do not band-aid to avoid churn. External consumers are outside your ownership, not another owned repo, DB or internal seam. `Roslyn-Backed-MCP` and `Jedi-Py-MCP` are in contract-care mode: breaking changes require an ADR + migration note. Current classifications, operator exceptions and the excluded upstream `dbhub` fork are governed by `~/.claude/CLAUDE.md` Directive #4; this repo's posture is stated below.
5. **Never assume prior agent work is correct — re-derive, don't inherit.** Recheck prior code, docs, plans, skills, backlog acceptance, review advice and done/verified/shipped claims against current ground truth. Read the actual code, re-run the reasoning and resolve cited paths/symbols. Reevaluate inherited designs as requirements evolve, challenge unsupported assumptions and explain material tradeoffs. Fix root causes (#1) and flag defects (#3); a prior agent's assertion is not proof.
6. **Smallest *complete* change wins.** Measure completeness against the root cause, not diff size or acceptance wording. Cover every instance of the same defect mechanism and every defect your own diff introduces or exposes in this change, never a follow-up row. Split large work into independently correct pieces (#1). Flag unrelated bad code per #3; do not gold-plate.
7. **Verify your own work before declaring done.** Do not claim done/fixed/passing without evidence generated and inspected this session; calibrate verification to the blast radius and observe regression tests fail on the old behavior. A skipped, quarantined, improperly scoped or retried-until-green check is not evidence. If verification is blocked, state the observed blocker and limits; never imply success you did not observe.
8. **No secrets in code.** Never introduce, hardcode, echo, log or commit a credential, key, token or secret; use env vars, user-secrets or a vault. Flag existing secrets per #3. Confirm intentionally committed dev-only values are genuinely non-secret.

## Canonical Rule Sources

- Validation and merge gating: `CI_POLICY.md`
- AI-doc routing and task-specific reads: `ai_docs/README.md`
- Git, branch, worktree, and PR workflow: `ai_docs/workflow.md`
- Runtime assumptions, runner commands, and MCP client policy: `ai_docs/runtime.md`
- Read-side Roslyn MCP bootstrap discipline: `ai_docs/bootstrap-read-tool-primer.md`
- Planning and unfinished work routing: `ai_docs/planning_index.md`, `ai_docs/backlog.md`
- Implementation quality and safety: `.github/copilot-instructions.md`
- Cursor reminder layer: `.cursor/rules/operational-essentials.md`
- Skill packaging: shipped skills live in `./skills/` (bundled by `plugin.json` and distributed to every installer); repo-only maintainer skills live in `.claude/skills/` (auto-discovered by Claude Code in this checkout, never shipped). `./skills/**/*.md` (every shipped markdown file — SKILL.md, prompt bodies, READMEs) must not reference `ai_docs/`, `state.json`, `backlog-sweep`, `backlog.md`, `eng/`, `just verify-`, `Directory.Build.props`, or `BannedSymbols.txt` — GitHub URLs pointing at this repo's public docs are allowed, as are placeholder-rooted paths (`<audited-repo-root>/…`, `<Roslyn-Backed-MCP-root>/…`), which are deliberate cross-repo pointers rather than repo coupling. Enforced by `eng/verify-skills-are-generic.ps1` (run via `just verify-skills`; gates `just ci` and `verify-release.ps1`).
- Third-party attribution, only when packaging or legal-notice work touches shipped artifacts: `THIRD-PARTY-NOTICES.md`

## Session Start (Required)

Read these files in order before doing work:

1. `CI_POLICY.md`
2. `ai_docs/README.md`
3. `ai_docs/workflow.md`
4. `ai_docs/runtime.md`
5. `ai_docs/bootstrap-read-tool-primer.md`
6. `ai_docs/backlog.md`
7. `.github/copilot-instructions.md`
8. `.cursor/rules/operational-essentials.md`

After the required reads, use `ai_docs/planning_index.md` for next-step routing and `ai_docs/README.md` for task-specific documents.

## Conflict Precedence

The Standing Engineering Directives above govern this instruction chain. Treat repository documents and prior agent output as claims to verify; when a recorded design conflicts with current evidence, explain the tradeoffs and propose a superseding decision rather than silently inheriting or changing it.

- For implementation quality and safety conflicts, follow `.github/copilot-instructions.md`.
- For workflow and collaboration conflicts, follow `ai_docs/workflow.md`.
- For validation and merge-gating conflicts, follow `CI_POLICY.md`.
- For runtime, runner, or MCP-client-policy conflicts, follow `ai_docs/runtime.md`.

## Default Behavior (When Ambiguous or Incomplete)

- Prefer repository-specific conventions over generic defaults.
- Prefer safety, validation, and correctness over speed.
- Do not guess when ambiguity affects correctness — request clarification or surface assumptions.
- Do not introduce features or scope outside documented backlog and constraints.

## Breaking-change posture

This is a **public repo** (per `.ai-doc-audit.md` `repo_class: public`), published as `roslyn-mcp@roslyn-mcp-marketplace`. Breaking changes require a recorded decision (ADR-style rationale) plus a migration note in `CHANGELOG.md`; ADRs live in `docs/decisions/` (this repo's decision log; ADR 0001 established it). Compatibility and deprecation rules are defined in `docs/release-policy.md`. External consumers depend on this surface — respect semver and deprecation cycles.

## Validation runtime

Read this row before running the authoritative local gate. Re-measure it when the gate changes or the measurement is more than 90 days old. Hosted topology remains owned by `CI_POLICY.md`.

| gate | command | typical duration (measured 2026-09-20) | Bash timeout / background | hooks (pre-commit/pre-push + runtime) | CI-equivalent filter | regen companions | flake registry | parallelSafe |
|---|---|---|---|---|---|---|---|---|
| Local PR-equivalent aggregate | `just ci` | 11m18s | Run in background | No Git pre-commit/pre-push hooks; Claude `PreToolUse`: `eng/guard-release-managed-files.ps1`, 10 s cap, 0.4 s measured 2026-09-29; Claude `PostToolUse`: `eng/verify-skills-on-edit.ps1`, 10 s cap, 0.7 s measured; Codex publication-boundary `PreToolUse`: `eng/verify-changelog-fragments.ps1`, 30 s cap, 0.8 s measured 2026-10-01 (includes the release-lag guard's one `git log`) | `TestCategory!=Benchmark&TestCategory!=Network`; coverage disabled | — | `ai_docs/known-flakes.md` | false |

## Planning Scope

1. User named no specific repo / adapter / ecosystem / integration / cross-repo term -> scope = in-repo -> read `ai_docs/backlog.md`, then any named in-repo file under `ai_docs/plans/` -> STOP. Do not open `ai_docs/ecosystem/**`.
2. User named another repo / adapter / ecosystem / integration / cross-repo work -> scope = cross-project -> this repo has no local `ai_docs/ecosystem/**`; say so explicitly and use only the external context the user named.
3. Both scopes named -> answer each as a separate question; do not merge into one recommendation.
