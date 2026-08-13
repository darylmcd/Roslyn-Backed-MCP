# AGENTS Bootstrap

Use this file as the stable AI bootstrap entry point for this repository.

## File Purpose (Critical)

This file is a bootstrap router, not a complete instruction set. Always execute **Session Start (Required)** before performing any task. Do not rely solely on this file; pull additional context as directed by the canonical rule sources below.

## Standing Engineering Directives

Restated from `~/.claude/CLAUDE.md` (canonical source). These eight directive **cores** (the bold titles) override expedience and are verbatim — do not summarize, drop, or alter them. The one-line gloss after each is a condensed summary for quick reference; the authoritative `Fires`/`Prevents`/`Edge` detail lives in `~/.claude/CLAUDE.md`.

1. **Correct fix > quick fix.** When a quick fix and a correct fix are both viable, choose the correct fix. The correct fix addresses the root cause; the quick fix patches a symptom. Quick fixes are only acceptable when the correct fix is genuinely out of scope for the current task — and when that happens, file a backlog row (per rule #3) for the correct fix before shipping the quick one. "We'll fix it later" without a tracked row = does not exist.

2. **Optimize for AI consumption by default.** 99% of files in this repo are AI-facing: `AGENTS.md`, `CLAUDE.md`, `.github/copilot-instructions.md`, `.github/prompts/**`, `ai_docs/**`, planning docs, runtime references, audit reports. Optimize them for fast, dense parsing: tables over prose, structured data over paragraphs, pointers over duplication, short imperative sentences over narrative. Human-facing files (`README.md` at landing-page level, `docs/**`) get human prose. Everywhere else, AI-optimized.

3. **Bad code is never silent — this rule fires in EVERY coding session.** Not just doc-audit. Not just code review. Every session where you read, edit, fix, refactor, debug, navigate, or otherwise touch code. When you observe bad code — dead code paths, swallowed exceptions, hardcoded values that should be config, hardcoded credentials/secrets, commented-out blocks, TODO/FIXME/HACK/XXX markers without a tracking row, obvious anti-patterns, security smells, broken naming, suspiciously stale comments, copy-paste duplication, god classes, layering violations, sync-over-async, untested critical paths — you MUST (a) call it out in your response and (b) recommend an appropriately-sized backlog row (≤4 production files, ≤3 test files, one regression shape). **Applies whether the bad code is the file you were sent to edit, an adjacent file you opened for context, an import target, a test file, or anything you read in the course of solving the task.** Critically: if you are FIXING a code section and it is bad/poorly-written/incorrect, surfacing it is mandatory even if the fix is "in scope" — the act of editing does not absolve the obligation to flag. The right *time* to fix the bad code is when the row's priority demands it, not when you happen to be in the file — but **the row MUST exist**. Silently editing around bad code, polishing a bad pattern without flagging, or "I'll mention it if asked" all count as misses.

4. **Private repos accept breaking changes.** For private repos (everything under `C:/Code-Repo/` EXCEPT this one), breaking changes and large refactors are ALWAYS acceptable when pursuing rule #1 or #3. This repo (`Roslyn-Backed-MCP`) is the PUBLIC exception, published as `roslyn-mcp@roslyn-mcp-marketplace`; breaking changes here require an ADR + migration note (see **Breaking-change posture**).

5. **Never assume prior agent work is correct — re-derive, don't inherit.** Work product from a previous Claude/agent session — code, docs, skills, prompts, backlog rows, plans, anything labeled "done"/"verified"/"shipped" — carries NO presumption of correctness. Treat it as a claim to check against current ground truth: read the actual code, re-run the reasoning, confirm cited paths/symbols still resolve. This fires with special force during model-handoff reviews (a newer model proof-reading an older model's work) and on anything asserted complete. Fix root causes (rule #1) and flag what you find (rule #3) rather than papering over inherited defects.

6. **Match change size to task value.** Correct ≠ maximal. #1 and #4 license root-cause fixes and breaking changes but do not mandate gold-plating — the smallest change that fully fixes the root cause wins. Flag adjacent bad code per #3 rather than fixing it inline.

7. **Verify your own work before declaring done.** Don't claim done/fixed/passing without evidence you generated this session (ran the test, read the output, exercised the path). Can't verify? Say so — don't imply success you didn't observe.

8. **No secrets in code.** Never introduce, hardcode, echo, log, or commit a credential, key, token, or secret — they live in env vars / user-secrets / a vault. Finding an existing one = flag per #3.

## Canonical Rule Sources

- Validation and merge gating: `CI_POLICY.md`
- AI-doc routing and task-specific reads: `ai_docs/README.md`
- Git, branch, worktree, and PR workflow: `ai_docs/workflow.md`
- Runtime assumptions, runner commands, and MCP client policy: `ai_docs/runtime.md`
- Read-side Roslyn MCP bootstrap discipline: `ai_docs/bootstrap-read-tool-primer.md`
- Planning and unfinished work routing: `ai_docs/planning_index.md`, `ai_docs/backlog.md`
- Implementation quality and safety: `.github/copilot-instructions.md`
- Cursor reminder layer: `.cursor/rules/operational-essentials.md`
- Skill packaging: shipped skills live in `./skills/` (bundled by `plugin.json` and distributed to every installer); repo-only maintainer skills live in `.claude/skills/` (auto-discovered by Claude Code in this checkout, never shipped). `./skills/**/SKILL.md` must not reference `ai_docs/`, `state.json`, `schemaVersion`, `backlog-sweep`, `backlog.md`, `eng/`, `just verify-`, `Directory.Build.props`, or `BannedSymbols.txt` — GitHub URLs pointing at this repo's public docs are allowed. Enforced by `eng/verify-skills-are-generic.ps1` (run via `just verify-skills`; gates `just ci` and `verify-release.ps1`).
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

## MCP Bootstrap

- Read `.mcp.json` after the required session-start files and before task-specific tool decisions.
- Treat `.mcp.json` as the repository's declared MCP intent, not as proof that a server is live in the current session.
- Always distinguish:
  1. Declared in `.mcp.json`
  2. Documented in repository docs (e.g. `ai_docs/runtime.md`)
  3. Verified live in the current session
- Prefer a verified live MCP server over shell or broad text search when available.
- If a server is declared in `.mcp.json` but unavailable at runtime, say so explicitly and continue with the best supported fallback.

## Conflict Precedence

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

This is a **public repo** (per `.ai-doc-audit.md` `repo_class: public`), published as `roslyn-mcp@roslyn-mcp-marketplace`. Breaking changes require a recorded decision (ADR-style rationale) plus a migration note in `CHANGELOG.md`; compatibility and deprecation rules are defined in `docs/release-policy.md`. External consumers depend on this surface — respect semver and deprecation cycles.

## Planning Scope

1. User named no specific repo / adapter / ecosystem / integration / cross-repo term -> scope = in-repo -> read `ai_docs/backlog.md`, then any named in-repo file under `ai_docs/plans/` -> STOP. Do not open `ai_docs/ecosystem/**`.
2. User named another repo / adapter / ecosystem / integration / cross-repo work -> scope = cross-project -> this repo has no local `ai_docs/ecosystem/**`; say so explicitly and use only the external context the user named.
3. Both scopes named -> answer each as a separate question; do not merge into one recommendation.
