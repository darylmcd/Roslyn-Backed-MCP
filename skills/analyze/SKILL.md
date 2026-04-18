---
name: analyze
description: "Solution health check. Use when: analyzing a C# solution or project, checking build health, finding diagnostics, assessing code quality, or getting an overview of a .sln/.csproj. Takes a path to a solution or project file as input."
user-invocable: true
argument-hint: "path to .sln, .slnx, or .csproj"
---

# Solution Health Analysis

You are a C# solution analyst. Your job is to load a workspace and produce a comprehensive, actionable health report.

## Input

`$ARGUMENTS` is the path to a `.sln`, `.slnx`, or `.csproj` file. If the user does not provide a path, search the current working directory for solution files and ask which one to analyze.

## Server discovery

When the tool list or workflows are unclear, call **`server_info`**, read the **`server_catalog`** resource (`roslyn://server/catalog`), or use MCP prompt **`discover_capabilities`** with category `analysis` or `all`.

**Repo shortcut:** from this repository root, `just verify-docs` and `just verify-version-drift` align with contributor doc/version checks (optional; not a substitute for loading the user's solution).

## Connectivity precheck

Before running any `mcp__roslyn__*` tool call, probe the server once:

1. Call `mcp__roslyn__server_info` — confirm the response includes `connection.state: "ready"`.
2. If the call fails OR `connection.state` is `initializing` / `degraded` / absent, bail with this message to the user and stop the skill:

   > **Roslyn MCP is not connected.** This skill requires an active Roslyn MCP server. Run `mcp__roslyn__server_heartbeat` to confirm connection state, then re-run this skill once the server reports `connection.state: "ready"`. See `ai_docs/runtime.md` § *Connection-state signals* for the canonical probes (`server_info` / `server_heartbeat`) and for the `mcp-connection-session-resilience` background.

3. If `connection.state` is `"ready"`, proceed with the rest of the workflow. The `server_info` call above also satisfies any server-version / capability-discovery needs — do not repeat it.

## Workflow

Execute these steps in order. Use the Roslyn MCP tools — do not shell out for analysis.

### Step 1: Load Workspace

1. Call `workspace_load` with the solution/project path.
2. Store the returned `workspaceId` for all subsequent calls.
3. Call `workspace_status` to confirm the workspace loaded successfully and note any load-time warnings.

### Step 2: Project Structure

1. Call `project_graph` to get the dependency structure.
2. Summarize: number of projects, dependency relationships, target frameworks.

### Step 3: Compilation Health

1. Call `compile_check` to run a fast in-memory compilation.
2. Call `project_diagnostics` with `severity: "Error"` to get all errors.
3. Call `project_diagnostics` with `severity: "Warning"` with `limit: 50` to get top warnings.
4. Summarize: total errors, total warnings, top diagnostic IDs by frequency.

### Step 4: Complexity Hotspots

1. Call `get_complexity_metrics` with `minComplexity: 10` and `limit: 20`.
2. For each result, note the method name, file, cyclomatic complexity, nesting depth, and maintainability index.
3. Flag methods with maintainability index below 40 or cyclomatic complexity above 20 as critical.

### Step 5: Cohesion Analysis

1. Call `get_cohesion_metrics` with `minMethods: 3` and `limit: 15`.
2. Flag types with LCOM4 > 1 as SRP violation candidates.
3. Note the independent method clusters for each flagged type.

### Step 6: Security & Vulnerabilities

1. Call `nuget_vulnerability_scan` to check for known CVEs.
2. Call `security_diagnostics` to get security-related compiler findings.
3. Summarize any findings by severity.

### Step 7: Close Workspace

1. Call `workspace_close` to release resources.

## Output Format

Present a structured report with these sections:

```
## Solution Health Report: {solution-name}

### Summary
- Projects: {count}
- Target Framework(s): {list}
- Compilation: {pass/fail} ({error-count} errors, {warning-count} warnings)
- Complexity Hotspots: {count} methods above threshold
- SRP Violations: {count} types with LCOM4 > 1
- Security Findings: {count}
- NuGet Vulnerabilities: {count}

### Compilation Issues
{table of top errors and warnings with file, line, diagnostic ID, message}

### Complexity Hotspots
{table of methods ranked by complexity: name, file:line, cyclomatic, nesting, maintainability}

### Cohesion Issues (SRP Candidates)
{table of types with LCOM4 > 1: type, file, LCOM4 score, cluster count}

### Security & Vulnerabilities
{list of findings with severity and remediation guidance}

### Recommendations
{prioritized list of actionable items}
```

Rank recommendations by impact: errors first, then security, then complexity, then cohesion.
