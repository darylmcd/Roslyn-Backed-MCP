# serialize-full-ci-premise-recheck — Re-decide parallel_safety.serializeFullCi now that full CI runs only on GitHub

**row:** `serialize-full-ci-premise-recheck` · **pri:** `Low` · **size:** `S`

## Anchors

- `ai_docs/prompts/backlog-sweep-addenda.md:77`

## Acceptance

- [ ] Confirm with the operator whether executors still run any local full gate (`just ci` / `ci_equivalent` / `executor-full-gate`) after the 2026-09-27 decision that full CI runs only on GitHub.
- [ ] If no local full gate runs: set `serializeFullCi: false` (keeping `parallelSafe: true`) and rewrite the rationale/scope_caveat, so `exec-args` stops forcing `maxParallel=1`.
- [ ] If some local full gate remains (e.g. landing `integration-gate`): keep `true` and record in the rationale which gate still needs the aggregate lock.
- [ ] Any change needed to the global `bsweep-state.mjs` exec-args semantics is filed in `~/.claude/ai_docs/backlog.md`, not here.

## Evidence

- HEAD 6f31f065 addenda `:76-77`: `parallelSafe: true` / `serializeFullCi: true`; rationale `:83-86`: "Full `just ci` / `ci_equivalent` runs remain serialized: they share resource-heavy build/test infrastructure".
- Global `~/.claude/scripts/bsweep-state.mjs` exec-args: `const ciAggregateLock = parallelSafe === false || readSerializeFullCi(addendaPath) === true; const serial = ciAggregateLock || maxParallelArg === 1;` → "exec-args forcing serial (maxParallel=1)".
- The 2026-09-27 operator decision (full CI on GitHub only, no local full gate) was relayed by the run orchestrator and is not yet recorded in the repo; the serialization premise must be re-derived before flipping. Filed investigate-first.

Source: backlog-remediate 20260926T234932Z follow-up.
