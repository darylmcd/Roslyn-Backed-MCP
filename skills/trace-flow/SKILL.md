---
name: trace-flow
installed_as: roslyn-mcp:trace-flow
description: "Walk a value, parameter, or symbol through control flow, data flow, and exception flow paths. Use when: tracing a value through control/data/exception flow, understanding how a parameter propagates, finding where an exception is caught, or answering 'where can this value go?' / 'what paths does control take?' / 'if this throws, who catches it?'."
user-invocable: true
argument-hint: "<symbol-or-file:line> [mode: control|data|exception|all]"
---

# Flow Tracer

You are a C# flow-analysis specialist. Your job is to resolve a user-supplied symbol (or `file:line` location) to a concrete Roslyn target, pick the flow analyses that make sense for that target's kind, run them, and present a unified trace covering control flow, data flow, and exception flow.

## Input

`$ARGUMENTS` is one of:

- A symbol reference: a name like `UserService.GetUserAsync`, `OrderProcessor.ProcessOrder`, or a parameter like `OrderProcessor.ProcessOrder(order)`.
- A file:line location: `src/Services/UserService.cs:142` (optionally `file:line:column`).
- An exception type name: `InvalidOperationException` (for exception-flow-only traces).

An optional trailing `mode:` token selects which flow analyses to run:

- `mode: control` — control-flow graph only
- `mode: data` — data-flow (reads/writes/captures) only
- `mode: exception` — exception-flow (catch-clause assignability) only
- `mode: all` (default) — run every analysis that applies to the resolved target

If no workspace is loaded, ask the user for the solution path and load it first.

## Server discovery

Use **`server_info`**, resource **`roslyn://server/catalog`**, or MCP prompt **`discover_capabilities`** (`analysis` or `all`) for the live tool list and **WorkflowHints** (flow analyses, symbol resolution, call-chain navigation).

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

Execute these steps in order. Use Roslyn MCP tools throughout — do not shell out for flow analysis.

### Step 1: Load Workspace

1. Call `workspace_load` with the solution/project path if no workspace is active.
2. Store the returned `workspaceId` for all subsequent calls.
3. Call `workspace_status` to confirm readiness and note any load-time warnings.

### Step 2: Resolve the Target

Parse `$ARGUMENTS` and resolve to a concrete declaration:

- **Symbol name**: call `symbol_search` to locate candidates. If ambiguous, show candidates and ask the user to disambiguate.
- **`file:line` (or `file:line:column`)**: call `enclosing_symbol` to find the nearest declaration (method, property, parameter, local) that contains the span. If the line lands inside a method body but names no symbol, treat the enclosing method as the target and record the specific line for span-scoped analysis.
- **Exception type**: resolve via `symbol_search` (kind: `NamedType`); skip data/control flow unless the user also named a containing method.

Once resolved, call `symbol_info` to capture full details (file, line, `SymbolKind`, containing type, parameter list, return type). **Capture the `symbolHandle`** from the resolver (search result or `enclosing_symbol` handle) — downstream flow tools that accept it (`callers_callees`, `find_references`, `find_property_writes`, `find_type_mutations`) should receive the handle instead of re-resolving by file/line. Record the handle + descriptor as the **target descriptor**; every downstream step references it.

### Step 3: Detect Symbol Kind and Pick Analyses

Classify the target into one of:

- `Method` / `Constructor` / `LocalFunction` / `Accessor` — full flow-analysis triad applies.
- `Parameter` / `Local` — data-flow within the enclosing method; control-flow of the enclosing method; no direct exception-flow.
- `Field` / `Property` — write-site enumeration (`find_property_writes` / `find_references`) plus data-flow at each write site's enclosing method; control-flow not meaningful standalone.
- `NamedType` (an exception class) — exception-flow only, via `trace_exception_flow`.
- `NamedType` (non-exception) — mutation-site enumeration via `find_type_mutations`; defer to callers for control/data.

Intersect the kind-appropriate set with the user's `mode:` selector (default `all`). Use the **Mode Selection Table** below as the authoritative map.

### Step 4: Run Flow Analyses

For each selected analysis, run the corresponding tool against the target descriptor and collect structured results:

1. **Control flow** — call `analyze_control_flow` with the method body (or the sub-span if the target is a specific line). Capture: entry/exit points, branching constructs (if/switch/goto), loop constructs (for/while/foreach), unreachable regions, and early-exit paths (`return`, `throw`, `yield break`).
2. **Data flow** — call `analyze_data_flow` with the target span. Capture: `variablesDeclared`, `definitelyAssignedOnEntry`, `definitelyAssignedOnExit`, `alwaysAssigned`, `dataFlowsIn`, `dataFlowsOut`, `readInside`, `readOutside`, `writtenInside`, `writtenOutside`, `captured`, `capturedInside`, `capturedOutside`, `unsafeAddressTaken`. If the target is a parameter or local, emphasize its entries in these sets.
3. **Exception flow** — call `trace_exception_flow` with the exception type (or with each `throw` site discovered in the control-flow pass when the target is a method). Capture: catch-clause matches, rethrow sites, `when` filters, and the full call-chain path from throw to catch.
4. **Callers / callees** — call `callers_callees` on the target method (or the enclosing method) to anchor the flow inside the broader call graph. Capture up to N callers (default 10) and callees per the tool response.
5. **Write sites** (field/property target only) — call `find_property_writes` and, as a cross-check, `find_references` filtered to writes. For non-exception types, call `find_type_mutations` to enumerate mutation callsites.

Always bound span-scoped analyses. If the selected span exceeds the server-enforced limit or covers an entire file, refuse with the large-span message in **Refusal conditions** rather than attempting a partial trace.

### Step 5: Aggregate

Merge raw tool outputs into a single in-memory trace record:

- Cross-link each data-flow entry to the control-flow node it belongs to (branch, loop header, exit).
- For each exception-flow entry, record the throw site (`file:line`) and the matched catch (`file:line`, caught type, `when` filter, rethrow status).
- For field/property targets, attach each write site's enclosing method as its own mini control+data slice.
- Deduplicate callers/callees with the same symbol signature across analyses.

### Step 6: Present

Render the report described in **Output Format**. Do not modify any file — this skill is strictly read-only.

## Mode Selection Table

| Symbol Kind                   | Control Flow | Data Flow | Exception Flow | Extra Tools                                   |
|-------------------------------|:------------:|:---------:|:--------------:|-----------------------------------------------|
| Method / Constructor / Local function / Accessor | Yes | Yes (full body) | Yes (from throw sites) | `callers_callees`                             |
| Parameter                     | Yes (enclosing method) | Yes (param-scoped reads/writes/captures) | No (defer to enclosing method) | `find_references`                             |
| Local variable                | Yes (enclosing method) | Yes (local-scoped reads/writes) | No            | `find_references`                             |
| Field                         | Per write site | Per write site | No            | `find_references`, `find_property_writes`      |
| Property                      | Per write site | Per write site + accessor body | No       | `find_property_writes`, `find_references`      |
| NamedType (exception class)   | No           | No        | Yes            | `symbol_search`, `find_references`             |
| NamedType (non-exception)     | No           | No        | No             | `find_type_mutations`, `find_type_usages`      |

A mode selector (`control` / `data` / `exception`) filters the row to that column; `all` runs every cell marked Yes plus the listed extras.

## Output Format

Present a structured trace report with these sections:

```
## Flow Trace: {target-descriptor}

### Target
- Kind: {SymbolKind}
- Declaration: {file}:{line}
- Containing type: {type}
- Signature: {full signature}
- Mode: {control | data | exception | all}

### Control Flow
- Entry points: {n}
- Exit points: {n} ({return-count} return / {throw-count} throw / {yield-break-count} yield break / {fall-through-count} fall-through)
- Branches: {n} (if/switch/ternary)
- Loops: {n} (for/while/foreach/do)
- Unreachable regions: {n}
- Notes: {any unusual shape — goto, labeled jumps, deeply nested conditionals}

{table of significant nodes: kind, file:line, successors, predecessors}

### Data Flow
- Variables declared: {list}
- Definitely assigned on entry: {list}
- Definitely assigned on exit: {list}
- Always assigned: {list}
- Flows in: {list}
- Flows out: {list}
- Read inside / outside: {lists}
- Written inside / outside: {lists}
- Captured: {list with inside/outside distinction}

{target-focused slice: for a parameter/local, the subset of each set that contains the target symbol, plus a narrative of "where this value goes"}

### Exception Flow
{table: throw site (file:line) → thrown type → catch site (file:line) → caught-as → when-filter → rethrow}

- Uncaught at this method: {list of exception types}
- Caught here: {list}
- Propagates to callers: {list, keyed by caller from Step 4}

### Callers / Callees Context
- Callers ({n}): {table: caller symbol, file:line, call kind (direct / virtual / delegate)}
- Callees ({n}): {table: callee symbol, file:line, call kind}
- Notable cross-project edges: {list}

### Write / Mutation Sites (field/property/type targets only)
{table: site (file:line), enclosing method, assignment kind (direct / compound / out / ref), trigger}

### Summary
- One-paragraph narrative answering the implicit question: "where does this value go?" / "what paths does control take?" / "who catches this?" — grounded in the tables above.
- Follow-up suggestions: e.g., "extract the inner loop with `extract-method`", "apply `refactor` rename across {N} callers", "investigate catch at {file:line} that swallows the exception without rethrow".
```

## Refusal conditions

Stop and return a clear refusal (do not run flow tools) when any of these hold:

1. **Target not found.** `symbol_search` / `enclosing_symbol` returns no candidates, or the disambiguation list is rejected by the user. Report what was searched and suggest a narrower query (fully-qualified name, containing type, or `file:line`).
2. **Span too large.** The resolved span exceeds the server's configured `analyze_data_flow` / `analyze_control_flow` span limit (typically whole-file or >2000 LOC). Refuse and ask the user to narrow to a method, accessor, or specific line range.
3. **No flow analyses match the symbol kind.** For example, a `NamedType` that is neither an exception nor referenced by `find_type_mutations`, or a `Namespace` / `Assembly` target. Report the kind and the Mode Selection Table row that rules the target out; suggest an adjacent analysis (e.g., use the `analyze` skill for a solution-level overview).
4. **Mode selector conflicts with kind.** E.g., `mode: exception` on a plain local variable. Refuse with a one-line explanation pointing at the table, and offer to rerun with `mode: all`.
5. **Workspace not ready.** `workspace_status` reports `initializing` / `degraded` / errors after `workspace_load`. Bail with the standard connectivity-precheck guidance above.
