---
name: architecture-review
description: "Layering and Dependency-Inversion-Principle audit of a C# solution. Use when: auditing layering, detecting cycles, checking DIP compliance, or finding cross-layer leaks. Takes an optional project or namespace filter as input."
user-invocable: true
argument-hint: "(optional) project or namespace filter"
---

# Architecture & Layering Review

You are a C# architecture auditor. Your job is to audit a loaded solution for layering violations, dependency cycles, DIP compliance, composition-root health, and cross-layer symbol leaks, then produce a prioritized, evidence-backed violations report.

## Input

`$ARGUMENTS` is an optional scope filter — either a project name or a namespace prefix (e.g. `Contoso.Billing` or `Contoso.Billing.Domain`). If omitted, audit the entire solution.

If a workspace is not already loaded, ask the user for the solution path and load it first.

## Server discovery

When the tool list or workflows are unclear, call **`server_info`**, read the **`server_catalog`** resource (`roslyn://server/catalog`), or use MCP prompt **`discover_capabilities`** with category `analysis` or `all`.

## Connectivity precheck

Before running any `mcp__roslyn__*` tool call, probe the server once:

1. Call `mcp__roslyn__server_info` — confirm the response includes `connection.state: "ready"`.
2. If the call fails OR `connection.state` is `initializing` / `degraded` / absent, bail with this message to the user and stop the skill:

   > **Roslyn MCP is not connected.** This skill requires an active Roslyn MCP server. Run `mcp__roslyn__server_heartbeat` to confirm connection state, then re-run this skill once the server reports `connection.state: "ready"`. See the [Connection-state signals reference](https://github.com/darylmcd/Roslyn-Backed-MCP/blob/main/ai_docs/runtime.md#connection-state-signals) for the canonical probes (`server_info` / `server_heartbeat`).

3. If `connection.state` is `"ready"`, proceed with the rest of the workflow. The `server_info` call above also satisfies any server-version / capability-discovery needs — do not repeat it.

## Workflow

Execute these steps in order. Use the Roslyn MCP tools — do not shell out for analysis.

### Step 1: Load Workspace

1. Call `workspace_load` with the solution/project path (or confirm via `workspace_status` if already loaded).
2. Store the returned `workspaceId` for all subsequent calls.
3. Call `workspace_status` to confirm the workspace loaded successfully and note any load-time warnings.
4. If the user supplied a scope filter, narrow all subsequent per-project calls to that project/namespace; otherwise iterate over every project returned by `project_graph`.

### Step 2: Project-Graph Cycle Detection

1. Call `project_graph` to get the full project dependency topology.
2. Walk the graph and record every strongly-connected component with more than one node — these are project cycles.
3. Also record self-loops (a project referencing itself via transitive rewrites — rare but possible).
4. Emit each cycle as a **Critical** violation with the participating project names and the edges that close the loop.

### Step 3: Namespace-Graph Per Project

For each in-scope project:

1. Call `get_namespace_dependencies` to get the within-project namespace graph.
2. Detect cycles the same way (SCC > 1). Each cycle is a **High** violation with the participating namespaces and representative edges.
3. Record the namespace-to-namespace edge list for use in Step 4.

### Step 4: Detect Improper Downward References

Use the layering heuristics (see below) to assign every project and namespace a layer rank (higher rank = higher-level / policy; lower rank = lower-level / mechanism).

For each edge where `caller.rank > callee.rank` is **violated** (i.e. a lower-level layer depends on a higher-level layer, or a peer layer reaches across an illegal boundary):

1. Call `symbol_relationships` against the offending namespace (or, when feasible, the specific type) to get granular symbol-level edges.
2. Call `find_references` on any concrete leak (e.g. a low-level project referencing a high-level DTO) to produce specific file/line evidence.
3. Record each leak as a **Medium** violation with the caller symbol, the callee symbol, the offending edge, and at least one file:line reference.

### Step 5: DI Composition-Root Audit

1. Call `get_di_registrations` to enumerate the DI container registrations across the solution.
2. Flag registrations where:
   - A high-level abstraction is bound to a concrete type from a sibling or higher-ranked layer (binding direction violates DIP).
   - The same interface has multiple conflicting registrations across composition roots.
   - A composition-root project depends on infrastructure concretes it should only know through abstractions.
3. For each suspicious registration, call `type_hierarchy` on the bound concrete type to confirm whether a matching abstraction already exists that the caller should depend on instead.
4. Record each as a **Medium** violation (promote to **High** if the registration introduces a project-graph cycle).

### Step 6: Reflection-Usage Flags

1. Call `find_reflection_usages` across the solution (or the filtered scope).
2. Reflection escapes the static type system and can hide layering violations — every occurrence is a **Low** severity flag with the file, symbol, and a one-line note on why it matters (e.g. "`Activator.CreateInstance` on an infrastructure type from a domain project").

### Step 7: Aggregate & Rank

Combine all findings from Steps 2 – 6. Deduplicate violations that share the same caller/callee edge. Rank strictly by severity (Critical → High → Medium → Low) and within a severity tier by the number of distinct references (more references = higher priority).

## Layering Heuristics

The skill infers "high vs low layer" in this order; stop at the first rule that yields a rank:

1. **Convention by project-name suffix** — ranks (higher number = higher level):
   - `.Domain` → 4
   - `.Application` / `.UseCases` → 3
   - `.Infrastructure` / `.Persistence` / `.Integration` → 2
   - `.Web` / `.Api` / `.Host` / `.Cli` / composition roots → 1
   - Test projects (`.Tests`, `.IntegrationTests`, etc.) are exempt from downward-reference checks but still participate in cycle detection.
2. **Convention by namespace segment** — the same suffix matching applied to the *terminal* namespace segment when the project itself is neutrally named.
3. **Dependency-direction fallback** — when no convention is detectable, infer rank from the project graph: a project is ranked higher than any project that depends on it. Ties break alphabetically; report the ambiguity in the final output.
4. **User override** — if the user supplied a scope filter, treat that project/namespace as the root (rank ceiling) for violation reporting, and note any cross-scope edges as informational rather than violations.

Always surface the heuristic used per project in the report so the user can override it on a follow-up run.

## Output Format

Present a structured report with these sections:

```
## Architecture & Layering Review: {solution-name}

### Summary
- Projects audited: {count}
- Scope filter: {filter or "whole solution"}
- Violations: {critical} Critical, {high} High, {medium} Medium, {low} Low
- Layering heuristic mix: {suffix-convention: N projects, namespace-convention: M, graph-fallback: P}

### Critical — Project Dependency Cycles
{table: cycle id, participating projects, closing edge(s), suggested cut}

### High — Namespace Dependency Cycles
{table: project, participating namespaces, representative symbol edges}

### Medium — Improper Downward References (DIP violations)
{table: caller symbol (project/namespace/type), callee symbol, caller-rank > callee-rank detail, file:line evidence, suggested abstraction}

### Medium — DI Composition-Root Issues
{table: registration (interface → concrete), composition root, issue (wrong-layer binding / duplicate / missing abstraction), suggested fix}

### Low — Reflection Usages
{table: file:line, symbol, reflection API, why it matters}

### Recommendations
{prioritized list of the top 5–10 actionable items, each pointing at a specific violation row above}
```

Each row in the Critical / High / Medium tables MUST include at least one concrete file:line reference produced via `find_references` or `symbol_relationships` — no unsupported assertions.

## Refusal conditions

Stop the skill and tell the user which condition tripped if any of the following are true:

1. **Workspace load failed** — `workspace_load` returned an error, or `workspace_status` reports an unrecoverable load failure. Ask the user to fix the solution path or underlying build error and re-run.
2. **Zero projects in the solution** — `project_graph` returned an empty project list (nothing to audit). Confirm the path points at a real `.sln` / `.slnx` / `.csproj` that contains at least one project.
