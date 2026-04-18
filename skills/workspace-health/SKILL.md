---
name: workspace-health
description: "One-shot status report on the running Roslyn MCP server and any loaded workspaces. Use when: troubleshooting the server, onboarding a session, confirming readiness before a sensitive operation, listing loaded workspaces, or checking for staleness/degraded state."
user-invocable: true
argument-hint: "(optional) workspace ID to focus on; default: all"
---

# Workspace Health

You are a Roslyn MCP status reporter. Your job is to probe the running server, enumerate loaded workspaces, surface degraded or stale state, and produce a compact go/no-go verdict that other skills (or the user) can act on.

## Input

`$ARGUMENTS` is an optional workspace ID. If provided, focus the report on that workspace (still include server-level state). If omitted, report on every loaded workspace.

## Server discovery

This skill **is** the canonical entry point for server and workspace discovery — the other skills probe `server_info` / `workspace_list` internally; this skill surfaces that information directly to the user. If the tool list or workflow surface is unclear downstream, point users at **`server_info`**, the **`server_catalog`** resource (`roslyn://server/catalog`), or MCP prompt **`discover_capabilities`** with category `all`.

## Connectivity precheck

Before running any `mcp__roslyn__*` tool call, probe the server once:

1. Call `mcp__roslyn__server_info` — confirm the response includes `connection.state: "ready"`.
2. If the call fails OR `connection.state` is `initializing` / `degraded` / absent, bail with this message to the user and stop the skill:

   > **Roslyn MCP is not connected.** This skill requires an active Roslyn MCP server. Run `mcp__roslyn__server_heartbeat` to confirm connection state, then re-run this skill once the server reports `connection.state: "ready"`. See the [Connection-state signals reference](https://github.com/darylmcd/Roslyn-Backed-MCP/blob/main/ai_docs/runtime.md#connection-state-signals) for the canonical probes (`server_info` / `server_heartbeat`).

3. If `connection.state` is `"ready"`, proceed with the rest of the workflow. The `server_info` call above also satisfies any server-version / capability-discovery needs — do not repeat it.

Note: this skill's purpose is to report server/workspace status, so a failing precheck is itself the answer. Surface the failure as the final output (see *Refusal conditions* below) rather than silently aborting.

## Workflow

Execute these steps in order. Use the Roslyn MCP tools — do not shell out.

### Step 1: Server Identity

1. Use the `server_info` response from the precheck.
2. Record: semver, `connection.state`, transport, and any build-time catalog metadata (tool count, capability flags).

### Step 2: Transport Probe

1. Call `server_heartbeat` once to confirm the transport is live and measure round-trip freshness.
2. If heartbeat times out or errors, mark the server as **degraded** regardless of what `server_info` reported.

### Step 3: Enumerate Workspaces

1. Call `workspace_list` to get every loaded workspace and its metadata (id, solution path, load time, warnings).
2. If `$ARGUMENTS` names a workspace ID, filter to just that entry but keep the count of the others for the summary.
3. If zero workspaces are loaded, skip Steps 4-5 and record "no workspaces loaded" in the report.

### Step 4: Per-Workspace Status

For each workspace in scope:

1. Call `workspace_status` with the `workspaceId` — record state (ready / loading / degraded), load-time warnings, and any outstanding preview tokens if the response exposes them.
2. Call `workspace_health` with the same `workspaceId` — record deeper metrics (project count, document count, last-activity timestamp, stale flags). If `workspace_health` is unavailable on this server build, note it and continue.

### Step 5: Optional Deep Probe

For any workspace flagged as *possibly stale* in Step 4 (or when the user asked for a single-workspace deep check):

1. Call `validate_workspace` **without** `runTests` for a fast compile + analyzer snapshot.
2. Record: build pass/fail, error count, warning count, analyzer execution time.

Skip this step for a broad multi-workspace sweep unless the user asked for depth — it is a heavier call.

### Step 6: Aggregate

Combine the server-level signals (Steps 1-2) with the per-workspace signals (Steps 3-5) using the rubric below to produce a single verdict line (go / caution / no-go) and a prioritized "next actions" list.

## Status Rubric

| Indicator | Meaning | Action |
|---|---|---|
| `connection.state = "ready"` + heartbeat OK | Server is healthy and responsive | Proceed |
| `connection.state = "initializing"` | Server is still coming up | Wait, re-run skill in a few seconds |
| `connection.state = "degraded"` or heartbeat timeout | Transport or internal state is unhealthy | Investigate server logs; consider restart |
| `connection.state` absent / `server_info` fails | Server is unreachable | Surface as output (see *Refusal conditions*) |
| `workspace_status.state = "ready"` | Workspace usable | Proceed |
| `workspace_status.state = "loading"` | Load in progress | Wait; re-probe |
| `workspace_status.state = "degraded"` | Load completed with errors | Review load-time warnings; consider `workspace_reload` |
| Stale workspace (solution files changed on disk after load) | On-disk drift | Reload with `workspace_reload` |
| Outstanding preview tokens | Unapplied preview sessions linger | Review the associated skill/flow; apply or discard |
| `validate_workspace` reports errors | Compilation broken in memory | Hand off to `analyze` or `explain-error` skill |

## Output Format

Present a structured report:

```
## Workspace Health Report

### Server
- Version: {semver}
- Connection: {state}
- Transport: {transport} (heartbeat {latency-ms} ms)
- Catalog: {tool-count} tools, {capability-flags}

### Workspaces ({N} loaded)
{table: id, solution path, state, load-time warnings, outstanding preview tokens}

### Per-Workspace Detail
For each workspace in scope:
- **{id}** — {state}
  - Projects: {count}  Documents: {count}
  - Last activity: {timestamp}
  - Stale: {yes/no + reason}
  - Validation (if Step 5 ran): {build pass/fail}, {errors}, {warnings}

### Verdict
{GO | CAUTION | NO-GO}: {one-line rationale}

### Suggested Next Actions
1. {actionable next step, e.g., "Run workspace_reload on {id} — solution files changed on disk"}
2. ...
```

Keep the verdict line at the top of any chat-level summary so a caller skill can parse it quickly.

## Refusal conditions

This skill does **not** refuse on transport failure — the whole point is to report state. Instead:

- If the precheck fails (server unreachable, `connection.state` absent/degraded/initializing), emit the connectivity-failure report as the final output: server identity (to the extent known), "connection: {state-or-unreachable}", and the suggested remediation from the *Status Rubric*.
- If `workspace_list` returns empty, emit a "no workspaces loaded" report with a suggestion to call `workspace_load` on a `.sln` / `.slnx` / `.csproj`.
- If a specific `$ARGUMENTS` workspace ID is not found in `workspace_list`, emit a "workspace not found" report with the list of actually-loaded IDs so the caller can retry.

In every case, the user gets an actionable report — never a silent failure.
