# Agent Guidelines

Roslyn-Backed-MCP is a local-first C# / .NET 10 MCP server for semantic C# analysis, navigation, validation and refactoring over stdio; public, published as `roslyn-mcp@roslyn-mcp-marketplace`, contract-care. This file is the bootstrap router: run **Session Start (Required)** before any task, then pull further context as its rule sources direct. Rules that hold in every repo live in the global layer (`~/.claude/CLAUDE.md`, mirrored to `~/.codex/AGENTS.md` and `~/.grok/AGENTS.md`); this file carries only this repo's facts.

## Standing Engineering Directives

Restated verbatim from `~/.claude/CLAUDE.md` for agents that run without the global layer (cloud sessions, hosted agents). Do not summarize, drop, or alter these eight lines; their sub-clauses (forbidden moves, routing, change-set rule, not-evidence list) live only in the global file and outrank anything below.

1. **Correct fix > quick or cheap fix.** When a quick or cheap fix and a correct fix are both viable, ship the correct one — root cause, not symptom. Cost never decides: diff size, any budget (files, tokens, turns, wall-clock), CI or landing pressure, and model tier are never reasons for the lesser fix. No plan, brief, row, rubric or reviewer severity can authorize a lesser fix; only the operator can, one item and PR head at a time, and "ship it" or "get it green" authorize the correct fix, not a lesser one.
2. **Optimize for AI consumption by default.** Nearly every document in these repos is AI-facing (`AGENTS.md`, `CLAUDE.md`, `.github/copilot-instructions.md`, `.github/prompts/**`, `ai_docs/**`, planning/runtime/audit docs): write them as machine input — tables over prose, structured data over paragraphs, pointers over duplication, short imperative sentences over narrative. Human-facing files (`README.md` landing pages, `docs/**`) get human prose. The density rules are doc-audit `STANDARD.md` §Quality Rules.
3. **Bad code is never silent.** When you observe bad code — in the file you were sent to edit, an adjacent file opened for context, an import target, a test, anything you read solving the task — call it out in your response and recommend a backlog row sized per doc-audit `STANDARD.md`. Editing a bad section does not absolve flagging it.
4. **Private repos accept breaking changes.** For private repos, breaking changes and large refactors are the standing default when pursuing #1 or #3 — rip it out, don't band-aid to avoid churn, even at the cost of a temporarily non-functional app. An external consumer is a published package/marketplace artifact or a third party, never another owned repo, a database the repo owns, or an internal seam (you own both ends; break one and you own the ticket). Which repos are public or in contract-care (ADR + migration note) is the **Repo posture** table in `~/.claude/CLAUDE.md`; only publication, or an operator decision recorded there, flips a repo.
5. **Never assume prior agent work is correct — re-derive, don't inherit.** Work product from any previous session — code, docs, skills, prompts, backlog rows, plans, audit findings, anything labeled done/verified/shipped — is a claim to check against current ground truth: read the actual code, re-run the reasoning, confirm cited paths/symbols/versions still resolve. Never take a commit message, comment, or doc's say-so as proof.
6. **Smallest *complete* change wins.** Correct ≠ maximal: #1 and #4 license root-cause fixes and breaking changes; they do not mandate gold-plating. Complete = the root cause + every instance of the same defect mechanism (same function, shared helper, or copy — found by probing the construct with references/grep and listed with `file:line`) + every defect your own diff introduces or exposes (duplication, weak or missing test, untested new branch, stale comment, suppression, shim, a new test that fails on a real bug). Measure against the root cause — not diff size, and not acceptance text that may encode the symptom.
7. **Verify your own work before declaring done.** Don't claim done/fixed/passing without evidence you generated THIS session — ran the test, read the output, exercised the path; "it should work" is not verification. Calibrate depth to blast radius. Can't verify (no runner, can't drive the UI)? Say so explicitly — never imply success you didn't observe.
8. **No secrets in code.** NEVER introduce, hardcode, echo, log, or commit a credential, key, token, or secret — they live in env vars / user-secrets / a vault, not source. Intentionally committed dev-only non-secrets (e.g. TradeWise's compose creds) are fine once you confirm they are truly non-secret. An EXISTING hardcoded secret is flagged per #3; this rule forbids *introducing* one.

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

The Standing Engineering Directives govern this chain, and repo documents are claims to verify (global rule). Between this repo's files:

- For implementation quality and safety conflicts, follow `.github/copilot-instructions.md`.
- For workflow and collaboration conflicts, follow `ai_docs/workflow.md`.
- For validation and merge-gating conflicts, follow `CI_POLICY.md`.
- For runtime, runner, or MCP-client-policy conflicts, follow `ai_docs/runtime.md`.

## Breaking-change posture

**Public repo** (`repo_class: public`), contract-care, published as `roslyn-mcp@roslyn-mcp-marketplace`: breaking changes require an ADR in `docs/decisions/` (this repo's decision log) plus a migration note in `CHANGELOG.md`; compatibility and deprecation rules are in `docs/release-policy.md`; respect semver and deprecation cycles.

## Validation runtime

Measured cost and wiring of every validation gate. Read a gate's row before running it, and size the Bash timeout from it. On the shared Windows dev host every gate runs through the machine-wide validation gate (global Environment rule); `just machine-validate [recipe]` is this repo's entry where it exists.

Hosted topology remains owned by `CI_POLICY.md`.

| gate | command | typical duration (measured 2026-10-15) | Bash timeout / background | hooks (pre-commit/pre-push + runtime) | CI-equivalent filter | regen companions | flake registry | parallelSafe |
|---|---|---|---|---|---|---|---|---|
| Local PR-equivalent aggregate | `just ci` | 36m21s (observed head8c303620) | Run in background | No Git pre-commit/pre-push hooks; Claude `PreToolUse`: `eng/guard-release-managed-files.ps1`, 10 s cap, 0.4 s measured 2026-09-29; Claude `PostToolUse`: `eng/verify-skills-on-edit.ps1`, 10 s cap, 0.7 s measured; Codex publication-boundary `PreToolUse`: `eng/verify-changelog-fragments.ps1`, 30 s cap, 0.8 s measured 2026-10-01 (includes the release-lag guard's one `git log`) | `TestCategory!=Benchmark&TestCategory!=Network`; coverage disabled | — | `ai_docs/known-flakes.md` | false |

## Planning Scope

1. User named no specific repo / adapter / ecosystem / integration / cross-repo term -> scope = in-repo -> read `ai_docs/backlog.md`, then any named in-repo file under `ai_docs/plans/` -> STOP. Do not open `ai_docs/ecosystem/**`.
2. User named another repo / adapter / ecosystem / integration / cross-repo work -> scope = cross-project -> this repo has no local `ai_docs/ecosystem/**`; say so explicitly and use only the external context the user named.
3. Both scopes named -> answer each as a separate question; do not merge into one recommendation.

## DevBuild-Runner pilot

- DevBuild-Runner pilot (operator-approved 2026-10-25; allowed option, not a mandate): the final full gate on clean merged `main` MAY run as runner job `roslyn-backed-mcp` / `ci` at the full pushed 40-character SHA.
  - Probe `server_info` once at run start and record the registered commands in the plan.
  - Submit with a fresh GUID `requestId`; poll `job_get` and read every `job_logs` page outside any held validation slot. The worker takes its own slot: never hold admission while waiting on a runner job.
  - Evidence = state `Succeeded`, exit 0, job worktree at that SHA. A submission alone is not evidence.
  - Until one batch records parity (runner pass/fail totals equal to local `just machine-validate ci` totals), run both and report both result lines. A mismatch is a runner defect row, never a pass.
  - Runner down or queue full: run the local gate and say so in the report. The runner does not replace hosted required checks and is never used to validate DevBuild-Runner itself.
