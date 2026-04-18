---
name: generate-tests
description: "Batch-scaffold test stubs for untested public APIs. Use when: batch-scaffolding tests for untested public APIs, generating test stubs, or bootstrapping a test project. Takes a top-N count, a project name, or a type name."
user-invocable: true
argument-hint: "[top-N | project name | type name]"
---

# Generate Tests

You are a C# test-scaffolding specialist. Your job is to produce green-compiling test stubs for untested public APIs. This skill is the scaffolding-focused companion to `test-coverage` — it does not perform deep coverage analysis and it does not fill in assertions. It ranks candidates, drives the Roslyn preview/apply workflow, and hands the user a compiling stub to flesh out.

## Input

`$ARGUMENTS` is one of:
- A number (e.g. `10`) — top-N untested public APIs ranked across the loaded workspace.
- A project name (e.g. `MyLib.Core`) — every untested public API in that project.
- A type name (e.g. `OrderProcessor`) — scaffold for that specific type's public methods.

If omitted, default to top-N = 5. If no workspace is loaded, ask for the solution path.

## Server discovery

Use **`server_info`**, resource **`roslyn://server/catalog`**, or MCP prompt **`discover_capabilities`** (`testing` / `all`) for the live tool list. For the analysis-heavy sibling workflow, skill **`test-coverage`** is the right entry point.

## Connectivity precheck

Before running any `mcp__roslyn__*` tool call, probe the server once:

1. Call `mcp__roslyn__server_info` — confirm the response includes `connection.state: "ready"`.
2. If the call fails OR `connection.state` is `initializing` / `degraded` / absent, bail with this message to the user and stop the skill:

   > **Roslyn MCP is not connected.** This skill requires an active Roslyn MCP server. Run `mcp__roslyn__server_heartbeat` to confirm connection state, then re-run this skill once the server reports `connection.state: "ready"`. See the [Connection-state signals reference](https://github.com/darylmcd/Roslyn-Backed-MCP/blob/main/ai_docs/runtime.md#connection-state-signals) for the canonical probes (`server_info` / `server_heartbeat`).

3. If `connection.state` is `"ready"`, proceed with the rest of the workflow. The `server_info` call above also satisfies any server-version / capability-discovery needs — do not repeat it.

## Workflow

### Step 1: Load Workspace

1. Call `workspace_load` with the solution/project path (or skip if already loaded).
2. Call `workspace_status` to confirm readiness.
3. Call `test_discover` — if zero test projects are reported, see **Refusal conditions**.

### Step 2: Identify Targets

Select the target set based on `$ARGUMENTS`:

- **Top-N (number or default 5):**
  1. Call `test_coverage` to pull the list of uncovered methods/types (fall back to `document_symbols` + `test_related` if the coverage collector is absent).
  2. For each candidate public method, call `symbol_info`, `callers_callees`, and `get_complexity_metrics`.
  3. Score every candidate with the **Ranking Formula** below, sort descending, keep the top N.
- **Project name:** enumerate public types via `document_symbols` across that project's files, filter to those where `test_related` returns zero tests, and score them for display order.
- **Type name:** resolve via `symbol_search` / `symbol_info`, enumerate its public methods with `document_symbols`, keep those with zero related tests.

Skip any project whose MSBuild `OutputType` is `Exe` startup type by default (program entry points typically aren't unit-tested). Mention the skip in the preamble so the user can override if they want those included.

### Step 3: Detect Sibling Test Pattern

For each target, find the nearest existing test file to use as `referenceTestFile` so the scaffold honors local conventions:

1. Identify the target's test project (matching name, `<Target>Tests`, or user-provided).
2. Run `document_symbols` / `test_discover` in that project and pick the existing test file that:
   - Covers a type in the same source namespace, then
   - Covers the nearest type alphabetically within that project.
3. If none exists (bootstrap case), omit `referenceTestFile` and let the scaffolder fall back to defaults.

### Step 4: Preview

- If N > 1 and `scaffold_test_batch_preview` is available, call it once with all targets to get a single preview token (atomic batch).
- Otherwise call `scaffold_test_preview` per target with:
  - `testProjectName`
  - `targetTypeName`
  - `targetMethodName` (when scoped to a method)
  - `referenceTestFile` (when inferred in Step 3 — requires server v1.22+)

Show the user the preview: files created, target types/methods, the inferred pattern source (if any).

### Step 5: Apply

After user confirmation:

1. Call `scaffold_test_batch_preview`'s companion apply tool, or `scaffold_test_apply` per preview token.
2. Call `compile_check` immediately to confirm stubs compile.
3. If compilation fails, stop and see **Refusal conditions**.

### Step 6: Report

List every scaffolded file, the target it covers, and compile status. Remind the user the stubs contain `Assert.Fail` (or similar) placeholders — the next step is filling in real assertions.

## Ranking Formula

```
score = 2 * complexity + 1 * ref_count + 3 * is_public + 10 * zero_related_tests
```

Where:
- `complexity` = cyclomatic complexity from `get_complexity_metrics`.
- `ref_count` = inbound edges from `callers_callees`.
- `is_public` = 1 if the symbol is public, 0 otherwise.
- `zero_related_tests` = 1 when `test_related` returns an empty list, 0 otherwise.

Rank descending, break ties by file:line ascending. Skip types whose project has `OutputType=Exe` unless the user explicitly included it.

## Output Format

```
## Test Scaffolding Report: {solution-name}

### Targets (ranked)
| # | Target | File:Line | Complexity | Refs | Public | Zero Tests | Score |
|---|--------|-----------|------------|------|--------|------------|-------|
| 1 | OrderProcessor.Apply | src/.../OrderProcessor.cs:42 | 14 | 7 | yes | yes | 48 |
| 2 | ... | ... | ... | ... | ... | ... | ... |

### Pattern Inference
- Target: `OrderProcessor.Apply` → reference test file: `tests/.../CustomerServiceTests.cs`
- Target: `InvoiceBuilder` → no sibling test; scaffolding from defaults

### Scaffolded Files
- `tests/.../OrderProcessorTests.cs` (new, compile: pass)
- `tests/.../InvoiceBuilderTests.cs` (new, compile: pass)

### Compile Status
{pass | fail with diagnostic summary}

### Next Steps
1. Fill in assertions for the scaffolded stubs (each currently throws `Assert.Fail`).
2. Re-run `test_run` once assertions are in place.
```

## Refusal conditions

Stop and report clearly if any of these hold:

- **No test projects found.** `test_discover` returned zero test projects. Recommend scaffolding one first (e.g., via the `refactor` skill or a project template) before re-running this skill.
- **No untested targets match scope.** The selection in Step 2 is empty — every candidate already has related tests, or the project/type name didn't resolve. Report the scope and exit.
- **Compile fails after scaffold.** Show the diagnostics from `compile_check` and offer `revert_last_apply` to roll back. Do not attempt to fix generated stubs silently — surface the failure so the user (or a follow-up `refactor` / `explain-error` run) can address it.
