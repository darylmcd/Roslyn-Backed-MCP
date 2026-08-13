---
name: architecture-review
installed_as: roslyn-mcp:architecture-review
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

## Step 8: Session Self-Check

After producing the final report, emit a summary:

```
summary: { semanticCalls: N, classificationApplied: <repo-stack> }
```

Determine `classificationApplied`:
- Glob CWD (depth=1) for `*.slnx`, `*.sln`, `*.csproj` → if any found: `"csharp"`
- Otherwise: `"unknown"`

Determine `semanticCalls`: count of Roslyn semantic tool calls made during this session (e.g. `project_graph`, `get_namespace_dependencies`, `symbol_relationships`, `find_references`, `get_di_registrations`, `type_hierarchy`, `find_reflection_usages`, etc.). Do NOT count `workspace_load`, `workspace_list`, `workspace_status`, or `server_info` — those are infrastructure calls, not semantic calls.

If `classificationApplied == "csharp"` AND `semanticCalls == 0`:
> **Warning:** This session operated on a C# repository but made zero Roslyn semantic tool calls. The architecture review may be incomplete. The following Steps were each expected to use specific Roslyn tools that were not called:
> - **Step 2** (`project_graph`) — project dependency topology and cycle detection
> - **Step 3** (`get_namespace_dependencies`) — namespace-level cycle detection per project
> - **Step 4** (`symbol_relationships`, `find_references`) — improper downward references and DIP violations
> - **Step 5** (`get_di_registrations`, `type_hierarchy`) — DI composition-root audit
> - **Step 6** (`find_reflection_usages`) — reflection escape points
>
> Consider re-running with `workspace_load` (called under the prefix pinned in *Connectivity precheck*) to enable semantic analysis for all steps.
