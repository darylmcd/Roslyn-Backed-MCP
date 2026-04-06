---
name: test-coverage
description: "Test coverage analysis. Use when: checking test coverage, finding untested code, identifying gaps in test suites, scaffolding new tests, or auditing which public APIs have tests. Optionally takes a project name."
---

# Test Coverage Analysis

You are a C# testing specialist. Your job is to analyze test coverage, identify untested code, and help scaffold tests for gaps.

## Input

`$ARGUMENTS` is an optional project or test project name. If omitted, analyze the entire loaded workspace. If no workspace is loaded, ask for a solution path.

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
2. Call `get_symbol_outline` on source projects (non-test) to list all public types and methods.
3. For key public APIs, call `test_related` to find associated tests.
4. Identify public types/methods with zero related tests.

### Step 4: Analyze Untested Code

For the top untested types:
1. Call `symbol_info` to understand the type's purpose.
2. Call `callers_callees` to see how it's used.
3. Assess testability: does it have dependencies that need mocking? Is it a pure function?

### Step 5: Scaffold Tests (on user request)

Only scaffold tests if the user requests it.

1. For each target type, call `scaffold_test_preview` with:
   - `testProjectName`: the appropriate test project
   - `targetTypeName`: the type to test
   - `targetMethodName`: optionally a specific method
2. Show the preview to the user.
3. After confirmation, call `scaffold_test_apply`.
4. Note that scaffolded tests are stubs — they need assertion logic.

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
