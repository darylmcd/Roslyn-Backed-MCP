---
name: analyze
installed_as: roslyn-mcp:analyze
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

### Step 7: Rank Top Issues

Aggregate every finding from Steps 3-6 into a single ranked list using this priority rubric:

| Tier | Weight | Source |
|------|--------|--------|
| P0 — Blockers | 100 | Compilation errors; Critical/High-severity NuGet CVEs; High-severity security diagnostics |
| P1 — High impact | 40-60 | Maintainability index < 40 OR cyclomatic complexity > 20; Medium-severity CVEs; Medium-severity security diagnostics |
| P2 — Medium impact | 15-30 | Cyclomatic complexity 10-20; LCOM4 > 1 with 3+ method clusters; most-frequent compiler warnings (top-5 IDs) |
| P3 — Low impact | 5-10 | Other warnings; LCOM4 > 1 with 2 clusters; informational analyzer hits |

Compose the Top-N Actionable Issues list: sort by tier then weight descending, break ties by file:line ascending, cap at 10-15 items. Each entry includes: tier, one-line description, file:line, suggested fix tool (e.g., `code_fix_preview` for a diagnostic ID, `refactor` skill for extract/rename, `dead-code` skill for unused).

### Step 8: Close Workspace

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

### Top-N Actionable Issues (ranked)
{table: #, tier (P0-P3), issue, file:line, suggested tool/skill}

### Compilation Issues
{table of top errors and warnings with file, line, diagnostic ID, message}

### Complexity Hotspots
{table of methods ranked by complexity: name, file:line, cyclomatic, nesting, maintainability}

### Cohesion Issues (SRP Candidates)
{table of types with LCOM4 > 1: type, file, LCOM4 score, cluster count}

### Security & Vulnerabilities
{list of findings with severity and remediation guidance}

### Recommendations
{the Top-N list above is the recommendation; restate the top 3 with next-action tool call}
```
