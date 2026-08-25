# addenda-ci-equivalent-self-hosted-runner-caveat — record the self-hosted-runner caveat in the addenda

**row:** `addenda-ci-equivalent-self-hosted-runner-caveat` · **pri:** `Medium` · **size:** `S`

## Anchors

- `ai_docs/prompts/backlog-sweep-addenda.md` (the `ci_equivalent` / Build-validation block)

## Acceptance

- [ ] The addenda's `ci_equivalent` entry carries an explicit caveat that this repo's CI runs on a self-hosted runner on the same machine, so a full local run of the release verification gate contends with it and produces spurious timing failures.
- [ ] The caveat states the preferred substitute for cold executors: `mcp__roslyn__compile_check` + targeted `mcp__roslyn__test_run --filter`, with PR CI as the authoritative gate.
- [ ] The caveat states that a timing-only failure which passes in isolation is contention, not breakage.

## Evidence

During plan `20260825T151721Z`, three Step-8b fix subagents were briefed to run the full local release verification gate (per the global execute prompt's generic `ci_equivalent` instruction). All three hit contention with the active runner: PR #1348's agent saw 4 timing/protocol failures, #1347's saw a 30s planner timeout, #1345's saw a 5.58s-vs-2s wall-clock assertion. All re-ran green in isolation. One agent explicitly flagged that the brief contradicted the standing operator memory.

## Context

The global `~/.claude/prompts/backlog-sweep-execute.md` tells executors to run `ci_equivalent` locally; only the repo addenda can carry the override, and today it does not. A cold subagent has no way to infer it. Recording it in the addenda is the single place that reaches every future executor.
