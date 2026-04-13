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

## Output

Give a short summary: failing tests, likely root cause, next command or tool calls, and whether **`compile_check`** is clean.
