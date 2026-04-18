---
name: exception-audit
description: "Repo-wide classification of exception handling patterns. Use when: auditing exception handling, finding swallowed exceptions, classifying catch patterns, finding overly-broad catches, or mapping unhandled-at-boundary throw sites in a C# solution. Optionally takes an exception type name to filter the audit."
user-invocable: true
argument-hint: "(optional) exception type filter; default: all"
---

# Exception Handling Audit

You are a C# exception-handling auditor. Your job is to load a workspace, enumerate every catch clause reachable from known exception types, classify each catch body by pattern (swallow, rethrow-as-is, rethrow-wrapped, delegate, handle), flag throw sites with no in-solution catch, and produce a ranked classification report that feeds downstream error-handling refactors.

## Input

`$ARGUMENTS` is an optional exception type name (e.g., `IOException`, `HttpRequestException`, `MyDomain.ValidationException`). If provided, the audit is scoped to catches assignable from that type and throw sites of that type. If omitted, audit every exception type in use across the solution, with special attention to `System.Exception` itself (the broadest catch).

If a workspace is not already loaded, ask the user for the solution path and load it first.

## Server discovery

Use **`server_info`**, resource **`roslyn://server/catalog`**, or MCP prompt **`discover_capabilities`** (`analysis` or `all`) for the live tool list and **WorkflowHints**. The primary tool for this skill is **`trace_exception_flow`** — confirm its presence in the catalog before starting.

## Connectivity precheck

Before running any `mcp__roslyn__*` tool call, probe the server once:

1. Call `mcp__roslyn__server_info` — confirm the response includes `connection.state: "ready"`.
2. If the call fails OR `connection.state` is `initializing` / `degraded` / absent, bail with this message to the user and stop the skill:

   > **Roslyn MCP is not connected.** This skill requires an active Roslyn MCP server. Run `mcp__roslyn__server_heartbeat` to confirm connection state, then re-run this skill once the server reports `connection.state: "ready"`. See the [Connection-state signals reference](https://github.com/darylmcd/Roslyn-Backed-MCP/blob/main/ai_docs/runtime.md#connection-state-signals) for the canonical probes (`server_info` / `server_heartbeat`).

3. If `connection.state` is `"ready"`, proceed with the rest of the workflow. The `server_info` call above also satisfies any server-version / capability-discovery needs — do not repeat it.

## Workflow

Execute these steps in order. Use the Roslyn MCP tools — do not shell out or grep the source tree for catch clauses.

### Step 1: Load Workspace

1. Call `workspace_load` with the solution/project path. Store the returned `workspaceId` for all subsequent calls.
2. Call `workspace_status` to confirm the workspace loaded successfully and note any load-time warnings. If the load fails, see **Refusal conditions** below.

### Step 2: Seed the Exception Type Set

1. If the user supplied a type filter in `$ARGUMENTS`, use `symbol_search` to resolve it to a fully-qualified type symbol. Confirm with `symbol_info`. If ambiguous, show candidates and ask the user to pick.
2. If no filter was supplied, seed the set with:
   - `System.Exception` (broadest catch — always audit).
   - Every exception type discovered by `symbol_search` for names ending in `Exception` inside the solution's source projects.
   - Framework exception types surfaced via `find_type_usages` on throw sites (see Step 4).

### Step 3: Enumerate Catch Clauses (primary)

For each exception type in the seed set:

1. Call `trace_exception_flow` with the exception type. This returns every catch clause assignable from that type, including:
   - File / line / enclosing method (use `enclosing_symbol` to confirm when the response lacks it).
   - The catch clause's declared exception type (may be a base type).
   - A body excerpt.
   - Rethrow-as annotations (`throw`, `throw new X(...)`, `throw;` — i.e., bare rethrow — and any wrapping chain).
2. Accumulate the catch clauses into a single table keyed by `(file, line, enclosingSymbol)`.
3. Separately, use `semantic_search` with queries such as `"catch (Exception"` and `"catch ("` to surface any catches the exception-flow trace missed (e.g., variable-less catches, filter expressions). Merge new entries into the accumulator.

### Step 4: Enumerate Throw Sites

For each exception type in the seed set:

1. Use `symbol_search` (kind `Method` / `Constructor`) plus `find_references` on the exception type's constructors to locate throw sites across the solution.
2. Record `(file, line, enclosingSymbol, exceptionType)` for each `throw new X(...)` and bare `throw;` outside a catch.

### Step 5: Classify Each Catch Body

For every catch clause captured in Step 3, classify the body using this procedure:

1. Call `analyze_control_flow` on the catch block span to determine whether it exits normally, rethrows, or returns.
2. Read the body excerpt from `trace_exception_flow` (or pull the enclosing span via `get_source_text` / `get_syntax_tree` if the excerpt is truncated).
3. Apply the **Classification Rubric** below to assign one of: `swallow`, `swallow-with-log`, `rethrow-as-is`, `rethrow-wrapped`, `delegate`, `handle-and-continue`, or `unknown`.
4. For `delegate` candidates (catch body calls another method that may itself throw), use `callers_callees` on the enclosing method to record the propagation chain one level up. Do not recurse indefinitely — one hop is enough for the report.

### Step 6: Identify Possible Unhandled-at-Boundary

For every throw site captured in Step 4:

1. Use `callers_callees` on the throwing method to walk the call graph upward.
2. At each frame, check whether an accumulated catch clause (from Step 3) covers the exception type (by assignability — use `symbol_info` / `type_hierarchy` if needed).
3. If the walk reaches a public entry point (program `Main`, ASP.NET controller action, `Task` returned from a top-level handler, test method, etc.) without finding a covering catch, flag the throw site as **possible unhandled-at-boundary**. Record the entry-point frame.
4. If the walk is cut off by recursion, virtual dispatch into unknown implementations, or a delegate boundary, mark the site `unknown-boundary` rather than `unhandled` — the classifier is conservative.

### Step 7: Aggregate and Rank

Aggregate Steps 3-6 into a single ranked report:

| Tier | Signal |
|------|--------|
| P0 — Critical | Empty `catch (Exception)` body (pure swallow of the broadest type); `catch (Exception)` that logs-and-continues at a domain boundary; unhandled-at-boundary throw of a non-recoverable exception type. |
| P1 — High | Non-empty `catch (Exception)` (broad catch) that does not rethrow; rethrow-wrapped that replaces `InnerException` with `null`; rethrow via `throw ex;` (stack-trace loss). |
| P2 — Medium | Typed swallow-with-log (e.g., `catch (IOException) { _log.Warn(...); }`); rethrow-as-is where a typed wrapper would be more appropriate; catch with a filter expression whose branches mix swallow and rethrow. |
| P3 — Low | Typed `handle-and-continue` with a sound recovery path; `delegate` catches whose inner method is a thin wrapper; informational / style-only findings. |

Sort by tier then by frequency-of-pattern descending; break ties by `file:line` ascending.

### Step 8: Close Workspace

Call `workspace_close` to release resources.

## Classification Rubric

| Pattern | Signal (what to look for in the catch body) | Severity |
|---------|---------------------------------------------|----------|
| **swallow** | Body is empty, or contains only a comment / `return;` / `return default;` with no log or side effect. `analyze_control_flow` reports normal exit. | P0 if `catch (Exception)`, else P1 |
| **swallow-with-log** | Body is exactly: log/trace call(s) and then falls through. No rethrow, no recovery state change. | P1 if `catch (Exception)`, else P2 |
| **rethrow-as-is (safe)** | Body ends with bare `throw;`. Stack trace preserved. | P3 |
| **rethrow-as-is (unsafe)** | Body ends with `throw ex;` (named variable) — overwrites the stack trace. | P1 |
| **rethrow-wrapped** | Body ends with `throw new X(..., ex);` where `ex` is passed as `innerException`. | P3 if wrapper is typed domain exception; P1 if `innerException` is dropped. |
| **delegate** | Body calls another method (e.g., `HandleError(ex)`, `Policy.Handle(ex)`) and the catch exits normally. Requires a one-hop `callers_callees` probe to resolve. | P2 |
| **handle-and-continue** | Body mutates recoverable state (e.g., sets a fallback, returns a default value) and exits normally. Typed catch only. | P3 |
| **broad-catch** | Clause is `catch (Exception)` or `catch` (no type / no variable) regardless of body — tag every such clause in addition to its body classification. | Promote the body's tier by one (min P1). |
| **unknown** | Control flow or body excerpt could not be classified confidently. | P2 (flag for manual review) |

## Output Format

Present a structured report with these sections:

```
## Exception Handling Audit: {solution-name}{ for {filter-type} if filtered}

### Summary
- Exception types audited: {count}
- Catch clauses found: {count}
- Throw sites found: {count}
- Possible unhandled-at-boundary: {count}
- Broad catches (`catch (Exception)` or variable-less): {count}

### Catch Population
{table — columns: Exception Type, Total Catches, Swallow, Swallow+Log, Rethrow-as-is, Rethrow-wrapped, Delegate, Handle, Unknown}

### Swallowed Exceptions
{table — columns: #, Tier, file:line, Enclosing Symbol, Catch Type, Body Excerpt (one-line), Notes}

### Broad Catches (`catch (Exception)` / variable-less)
{table — columns: #, Tier, file:line, Enclosing Symbol, Body Classification, Rationale}

### Possible Unhandled-at-Boundary
{table — columns: #, Throw Site (file:line), Exception Type, Enclosing Method, Reached Boundary, Propagation Chain}

### Recommendations
{top 3-5 refactors — e.g., "Replace `throw ex;` at X with bare `throw;` (use refactor skill)", "Narrow `catch (Exception)` at Y to `catch (IOException, TimeoutException)`", "Wrap throw site Z in a typed domain exception at the API boundary". Each recommendation cites the suggested next tool or skill (e.g., `code-actions`, `refactor`, `code_fix_preview` for a diagnostic ID).}
```

## Refusal conditions

If `workspace_load` fails or `workspace_status` reports the workspace did not load, stop the skill and report the failure with any load-time diagnostics returned by `workspace_status`. Do not attempt any of Steps 2-8 without a loaded workspace — the catch-graph enumeration requires semantic symbols that are only available after a successful load.
