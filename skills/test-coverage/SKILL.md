---
name: test-coverage
installed_as: roslyn-mcp:test-coverage
description: "Test coverage analysis. Use when: checking test coverage, finding untested code, identifying gaps in test suites, scaffolding new tests, or auditing which public APIs have tests. Optionally takes a project name."
user-invocable: true
argument-hint: "[optional project or test project name]"
---

# Test Coverage Analysis

You are a C# testing specialist. Your job is to analyze test coverage, identify untested code, and help scaffold tests for gaps.

## Input

`$ARGUMENTS` is an optional project or test project name. If omitted, analyze the entire loaded workspace. If no workspace is loaded, ask for a solution path.

## Server discovery

Use **`discover_capabilities`** (`testing` / `all`) or MCP prompt **`review_test_coverage`**. For red CI focused on failing tests first, skill **`test-triage`** is a lighter entry point.

## Connectivity precheck

Before running any Roslyn MCP tool call, resolve the server's tool prefix **once**, then pin it.

> **The prefix is client-assigned — never hard-code it.** Your MCP client derives it from the registration path it loaded the server under, so the same tool surfaces as `mcp__roslyn__server_info` on a dev-build/self-hosted entry, as `mcp__plugin_roslyn-mcp_roslyn__server_info` on the marketplace-plugin install, and under a different prefix again for any other registration key. Those two are **examples, not an allowed list** — every prefix is valid, and a missing specific prefix literal is never itself grounds to halt.

1. **Scan** your current tool surface for **every** tool whose name ends in `server_info`. `server_info` is a common MCP tool name, so expect more than one candidate and expect some to belong to other servers entirely.
2. **Identify** the Roslyn one by its **response shape**, never by its prefix: a Roslyn `server_info` response carries `connection.state`, `catalogVersion`, and `surface.*`. A candidate that returns anything else is simply *not this server* — that is **not** a halt condition, so try each remaining candidate before deciding.
3. **Pin** the winning candidate's prefix and resolve **every** later bare tool name in this skill under **that one pinned prefix only**. Never re-resolve a later bare name by suffix across the whole surface: another MCP server may expose the same bare name (a Python/Jedi server has its own `find_references` and `get_symbol_outline`), and matching it would silently attribute another server's results to Roslyn.
4. Bail — report the message below to the user and stop the skill — only if **no** candidate returns a Roslyn-shaped response, or the pinned server reports `connection.state` as anything other than `"ready"` (`initializing`, `degraded`, absent):

   > **Roslyn MCP is not connected.** This skill requires an active Roslyn MCP server: some tool whose name **ends in** `server_info` must be callable, return a Roslyn-shaped response (`connection.state`, `catalogVersion`, `surface.*`), and report `connection.state: "ready"`. The prefix does not matter — your client assigns it from the registration path, so it may be `mcp__roslyn__…`, `mcp__plugin_roslyn-mcp_roslyn__…`, or anything else. Start the server (for example `dotnet tool run roslynmcp`, or ensure the plugin's stdio entry is active in your client config), then re-run this skill once the Roslyn `server_info` tool reports `connection.state: "ready"` under whichever prefix your tool surface exposes. See the [Connection-state signals reference](https://github.com/darylmcd/Roslyn-Backed-MCP/blob/main/ai_docs/runtime.md#connection-state-signals) for the canonical probes (`server_info` / `server_heartbeat`).

5. Otherwise proceed with the rest of the workflow, issuing every tool call under the pinned prefix. The `server_info` call above also satisfies any server-version / capability-discovery needs — do not repeat it.

## Workflow

### Step 1: Discover Tests

1. Ensure a workspace is loaded.
2. Call `test_discover` to find all test cases in the solution.
3. Summarize: total test count, test projects, test frameworks detected.

### Step 2: Run Tests with Coverage

1. Call `test_coverage` with the optional project filter.
2. If `coverlet.collector` is not installed, note this and fall back to `test_run` for pass/fail only.
3. Parse coverage results: line coverage, branch coverage, per-module and per-class breakdown.

### Step 3: Identify Coverage Gaps

1. From coverage data, find classes and methods with low coverage (< 50% line coverage).
2. Call `document_symbols` on key source files (non-test) to list declared symbols.
3. For key public APIs, call `test_related` to find associated tests.
4. Identify public types/methods with zero related tests.

### Step 4: Analyze Untested Code

For the top untested types:
1. Call `symbol_info` to understand the type's purpose.
2. Call `callers_callees` to see how it's used.
3. Assess testability: does it have dependencies that need mocking? Is it a pure function?

### Step 5: Rank & Scaffold Tests

Rank the untested public APIs from Steps 3-4 using this priority rubric:

| Tier | Signal | Weight |
|------|--------|--------|
| P0 — critical gap | public method with cyclomatic complexity >= 10 AND zero related tests | 100 |
| P1 — broadly used | public method called by >= 3 other methods AND zero related tests | 50 |
| P2 — orphan type | public type with no related tests whose enclosing project has other tests | 25 |
| P3 — remaining | other untested public APIs | 5 |

Present the top 5-10 as a ranked list with tier label, type/method name, file:line, and the signal that landed it there.

Then prompt the user: "Scaffold tests for the top N?" (default N=5 if the user says yes without a number). If the user agrees OR the user invoked this skill with `--scaffold-top=N`:

1. For each target, call `scaffold_test_preview` with:
   - `testProjectName`: the appropriate test project
   - `targetTypeName`: the type to test
   - `targetMethodName`: optionally a specific method
   - `referenceTestFile`: a sibling test file when the pattern is inferable (v1.22+)
2. If `scaffold_test_batch_preview` is available and N > 1, prefer batch mode for a single preview token.
3. Show the preview(s) to the user.
4. After confirmation, call `scaffold_test_apply` for each token.
5. Call `compile_check` to verify the scaffolded tests compile.
6. Note: scaffolded tests are stubs — they need real assertions before they add value.

### Step 6: Related Test Lookup

If the user provides changed files:
1. Call `test_related_files` with the file paths.
2. Return the filter expression for running only affected tests.
3. Suggest: `test_run` with the filter to validate changes.

## Output Format

```
## Test Coverage Report: {solution-name}

### Summary
- Test Projects: {count}
- Total Tests: {count}
- Overall Line Coverage: {percent}%
- Overall Branch Coverage: {percent}%

### Coverage by Module
{table: project, line%, branch%, uncovered lines}

### Coverage by Class (lowest coverage)
{table: class, file, line%, branch%, key untested methods}

### Untested Public APIs
{table: type/method, file:line, complexity, suggestion}

### Test Scaffolding Opportunities
{list of types where scaffold_test_preview can generate stubs}

### Recommendations
1. {highest-impact coverage gap}
2. {next priority}
...
```

## Guidelines

- Coverage percentages are guides, not goals. 100% coverage doesn't mean bug-free code.
- Focus on high-value coverage gaps: complex logic, error handling, edge cases.
- Note when a low-coverage class is a DTO/model that doesn't need behavioral tests.
- Scaffolded tests are starting points — always note they need real assertions.
