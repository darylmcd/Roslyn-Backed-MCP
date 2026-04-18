---
name: test-triage
description: "Test discovery and failure triage. Use when: CI is red, tests fail locally, or you need to find and run the right tests after a change. Optionally takes project name or filter."
user-invocable: true
argument-hint: "[optional project name or test filter]"
---

# Test Triage

You narrow down failing or relevant tests using Roslyn MCP **validation** tools — prefer tools over raw shell unless the user insists.

## Input

`$ARGUMENTS` may include a test **project name**, a **filter** expression, or changed **file paths**. If missing, start from full discovery then narrow.

## Server discovery

Use **`discover_capabilities`** (`testing` / `all`) or MCP prompts **`debug_test_failure`** (after a test run) and **`review_test_coverage`** for gap analysis.

## Workflow

1. **`workspace_load`** if needed; **`workspace_status`** for health.
2. **`test_discover`** — summarize frameworks, projects, and test counts.
3. **`test_run`** — full suite, or scoped with project/filter from arguments.
4. On failures: use output stack traces; **`symbol_search`** / **`go_to_definition`** to jump to code; MCP prompt **`debug_test_failure`** to structure the next steps.
5. After local edits: **`test_related`** for symbols or **`test_related_files`** for changed file paths, then **`test_run`** on the smallest useful scope.
6. Optionally **`build_workspace`** if compilation state is uncertain before tests.

## Flake detection mode

Invoke with `--detect-flakes[=N]` (default N=5) or ask "is this test flaky?" to bucket failing tests by determinism.

1. After Step 3 produces a list of failures, take the failing test filter and call `test_run` with that filter **N times in sequence**. Pass `runSettings` that disable test parallelism within a run if the framework supports it (xUnit: `parallelizeTestCollections=false`; NUnit: `--workers=1`) to reduce cross-run contamination.
2. Bucket each failing test across the N runs:
   - **deterministic-fail**: failed in N/N runs — real bug, fix the assertion or the system under test
   - **flake**: failed in 1 < k < N runs — flaky, likely timing/ordering/state-leak
   - **transient-pass**: failed once and passed in all retries — likely environmental (clock skew, tmp dir race)
3. Produce a per-test row with bucket, pass-rate (k passes / N), and suggested next action:
   - deterministic-fail → inspect the assertion + surrounding code via `symbol_info` / `get_source_text`
   - flake → look for `DateTime.Now`, `Task.Delay`, `new Random()` without seed, shared statics in the SUT — scan via `symbol_search` on likely suspects
   - transient-pass → annotate as suspected flake; do not act unless it recurs

Honor a cap on N (default max 20) to avoid runaway CI time. If the user requests a huge N, warn and ask.

## Output

Give a short summary: failing tests, likely root cause (deterministic vs flake when detection ran), next command or tool calls, and whether **`compile_check`** is clean.
